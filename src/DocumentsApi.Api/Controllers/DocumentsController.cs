using System.Security.Cryptography;
using DocumentsApi.Api.Dtos;
using DocumentsApi.Api.Errors;
using DocumentsApi.Api.Validation;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Options;
using DocumentsApi.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocumentsApi.Api.Controllers;

/// <summary>
/// This API keeps no copy of the PDF and no separate "document" record -
/// the only thing persisted is the signing log itself (one
/// <see cref="DocumentSignature"/> row per completed stage) plus a log of
/// failed signing attempts. A document's identity is its file name; its
/// state is whatever signatures are logged against that name.
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private const long RequestSizeLimitCeilingBytes = 100 * 1024 * 1024;

    private readonly IDocumentSignatureRepository _signatureRepository;
    private readonly IDocumentProcessingService _documentProcessingService;
    private readonly DocumentUploadOptions _uploadOptions;

    public DocumentsController(
        IDocumentSignatureRepository signatureRepository,
        IDocumentProcessingService documentProcessingService,
        IOptions<DocumentUploadOptions> uploadOptions)
    {
        _signatureRepository = signatureRepository;
        _documentProcessingService = documentProcessingService;
        _uploadOptions = uploadOptions.Value;
    }

    /// <summary>
    /// Validates a new document and prepares it for signing. Stamps a blank
    /// three-row signature table onto it and returns the result for the
    /// frontend to display. Nothing is written to the database here - only
    /// signing itself is logged.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(RequestSizeLimitCeilingBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.File.Length == 0)
        {
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        if (request.File.Length > _uploadOptions.MaxFileSizeBytes)
        {
            return this.Error(
                StatusCodes.Status400BadRequest,
                ErrorCodes.FileTooLarge,
                $"The uploaded file exceeds the maximum allowed size of {_uploadOptions.MaxFileSizeBytes / (1024 * 1024)} MB.",
                new { maxSizeBytes = _uploadOptions.MaxFileSizeBytes });
        }

        var isPdf = string.Equals(request.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(request.File.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf)
        {
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.UnsupportedFileType, "Only PDF files are supported.");
        }

        var baseFileName = Path.GetFileNameWithoutExtension(request.File.FileName);
        if (!DocumentFileNameValidator.TryParse(baseFileName, out _))
        {
            return this.Error(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidFileName,
                "File name must follow the '<RepositoryId>#<ProjectName>#<Version>.pdf' convention, " +
                "e.g. '729#VIPD2#v1.00.16.pdf'.");
        }

        byte[] sourceBytes;
        using (var memoryStream = new MemoryStream())
        {
            await request.File.CopyToAsync(memoryStream, cancellationToken);
            sourceBytes = memoryStream.ToArray();
        }

        var outcome = _documentProcessingService.PrepareForSigning(baseFileName, sourceBytes);
        if (!outcome.IsValid)
        {
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.FileProcessingFailed, outcome.ErrorMessage!);
        }

        Response.Headers["X-File-Name"] = baseFileName;
        Response.Headers["X-Next-Expected-Category"] = SignatureCategoryExtensions.Sequence[0].ToString();

        // No Content-Disposition/file name here: this response is meant to be
        // displayed to the user (e.g. an embedded PDF viewer), not downloaded.
        return File(outcome.RenderedBytes!, "application/pdf");
    }

    /// <summary>
    /// Signing status for a document, identified by file name: which
    /// categories are signed, by whom, and what's expected next. There's no
    /// separate document id - the file name is the only identity there is.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.FileNameRequired, "fileName is required.");
        }

        var signatures = await _signatureRepository.GetByFileNameAsync(fileName, cancellationToken);
        return Ok(ToStatusResponse(fileName, signatures));
    }

    /// <summary>
    /// Signs a document for one category. The document is identified by the
    /// uploaded file's own name (same "&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;"
    /// convention as upload) - there's no separate document id to pass. For
    /// the second and third stage, the submitted file must hash-match the
    /// previous stage's logged result, so a stale or tampered copy is
    /// rejected rather than silently signed over. Categories must be signed
    /// in order (Opracował, then Sprawdził, then Zatwierdził) and the caller
    /// must hold the role mapped to the requested category. The confirmation
    /// dialog is a frontend concern; this call happens once the user
    /// confirms. The resulting PDF is returned for the user to download.
    /// </summary>
    [HttpPost("sign")]
    [RequestSizeLimit(RequestSizeLimitCeilingBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Sign([FromForm] SignDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.File.Length == 0)
        {
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        var baseFileName = request.FileName;
        if (!DocumentFileNameValidator.TryParse(baseFileName, out var fileNameParts))
        {
            return this.Error(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidFileName,
                "fileName must follow the '<RepositoryId>#<ProjectName>#<Version>' convention, " +
                "e.g. '729#VIPD2#v1.00.16' - use the value from the X-File-Name header returned by upload.");
        }

        byte[] currentBytes;
        using (var memoryStream = new MemoryStream())
        {
            await request.File.CopyToAsync(memoryStream, cancellationToken);
            currentBytes = memoryStream.ToArray();
        }

        var command = new SignDocumentCommand(
            baseFileName,
            fileNameParts.RepositoryId,
            fileNameParts.ProjectName,
            fileNameParts.Version,
            request.Category,
            User,
            currentBytes);

        var outcome = await _documentProcessingService.SignAsync(command, cancellationToken);
        if (!outcome.IsValid)
        {
            var reason = outcome.FailureReason!.Value;
            return this.Error(StatusCodeFor(reason), ErrorCodeFor(reason), outcome.Message!, outcome.ErrorData);
        }

        Response.Headers["X-Fully-Signed"] = outcome.IsFullySigned.ToString();
        if (outcome.NextExpectedCategory is not null)
        {
            Response.Headers["X-Next-Expected-Category"] = outcome.NextExpectedCategory.Value.ToString();
        }

        return File(outcome.RenderedBytes!, "application/pdf", $"{baseFileName}.pdf");
    }

    /// <summary>
    /// Hash-comparison tool: uploads a PDF, hashes it, and reports whether it
    /// matches a signing event this API has logged.
    /// </summary>
    [HttpPost("verify")]
    [RequestSizeLimit(RequestSizeLimitCeilingBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Verify([FromForm] UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.File.Length == 0)
        {
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        byte[] bytes;
        using (var memoryStream = new MemoryStream())
        {
            await request.File.CopyToAsync(memoryStream, cancellationToken);
            bytes = memoryStream.ToArray();
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var signature = await _signatureRepository.FindByHashAsync(hash, cancellationToken);

        if (signature is null)
        {
            return Ok(new VerifyDocumentResponse { Found = false });
        }

        return Ok(new VerifyDocumentResponse
        {
            Found = true,
            FileName = signature.FileName,
            MatchedStage = signature.Category.DisplayName(),
            SignedBy = signature.SignedBy,
            SignedAtUtc = signature.SignedAtUtc,
        });
    }

    private static int StatusCodeFor(SigningFailureReason reason) => reason switch
    {
        SigningFailureReason.RoleNotAuthorized => StatusCodes.Status403Forbidden,
        SigningFailureReason.AlreadySigned or SigningFailureReason.OutOfOrder or SigningFailureReason.StaleDocumentState
            => StatusCodes.Status409Conflict,
        SigningFailureReason.ProcessingFailed => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static string ErrorCodeFor(SigningFailureReason reason) => reason switch
    {
        SigningFailureReason.AlreadySigned => ErrorCodes.AlreadySigned,
        SigningFailureReason.OutOfOrder => ErrorCodes.OutOfOrderSignature,
        SigningFailureReason.RoleNotAuthorized => ErrorCodes.RoleNotAuthorized,
        SigningFailureReason.StaleDocumentState => ErrorCodes.StaleDocumentState,
        SigningFailureReason.ProcessingFailed => ErrorCodes.SigningFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static DocumentStatusResponse ToStatusResponse(string fileName, IReadOnlyList<DocumentSignature> signatures)
    {
        var isFullySigned = SignatureCategoryExtensions.IsFullySigned(signatures);
        SignatureCategory? nextExpected = isFullySigned
            ? null
            : SignatureCategoryExtensions.GetNextExpected(signatures);

        return new DocumentStatusResponse
        {
            FileName = fileName,
            Signatures = signatures
                .OrderBy(s => s.Category)
                .Select(s => new SignatureStatusEntry
                {
                    Category = s.Category.DisplayName(),
                    SignedBy = s.SignedBy,
                    SignedAtUtc = s.SignedAtUtc,
                })
                .ToList(),
            CurrentHash = signatures.OrderByDescending(s => s.Category).FirstOrDefault()?.DocumentHash,
            NextExpectedCategory = nextExpected?.DisplayName(),
            IsFullySigned = isFullySigned,
        };
    }
}

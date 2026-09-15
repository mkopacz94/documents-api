using System.Security.Cryptography;
using DocumentsApi.Api.Dtos;
using DocumentsApi.Api.Errors;
using DocumentsApi.Api.Validation;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Options;
using DocumentsApi.Core.Services;
using DocumentsApi.Core.Services.Signing;
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
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentSignatureRepository signatureRepository,
        IDocumentProcessingService documentProcessingService,
        IOptions<DocumentUploadOptions> uploadOptions,
        ILogger<DocumentsController> logger)
    {
        _signatureRepository = signatureRepository;
        _documentProcessingService = documentProcessingService;
        _uploadOptions = uploadOptions.Value;
        _logger = logger;
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
            _logger.LogInformation("Upload rejected: empty file.");
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        if (request.File.Length > _uploadOptions.MaxFileSizeBytes)
        {
            _logger.LogInformation(
                "Upload rejected: file size {FileSizeBytes} exceeds the {MaxSizeBytes} limit.",
                request.File.Length, _uploadOptions.MaxFileSizeBytes);
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
            _logger.LogInformation("Upload rejected: unsupported content type {ContentType}.", request.File.ContentType);
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.UnsupportedFileType, "Only PDF files are supported.");
        }

        var baseFileName = Path.GetFileNameWithoutExtension(request.File.FileName);
        if (!DocumentFileNameValidator.TryParse(baseFileName, out _))
        {
            _logger.LogInformation("Upload rejected: file name {FileName} does not match the naming convention.", baseFileName);
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
            // The underlying PDF failure itself is already logged inside
            // DocumentProcessingService - this just records the HTTP outcome.
            _logger.LogInformation("Upload rejected: {FileName} could not be processed.", baseFileName);
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.FileProcessingFailed, outcome.ErrorMessage!);
        }

        _logger.LogInformation("Uploaded {FileName} and prepared it for signing.", baseFileName);

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
            _logger.LogInformation("Status request rejected: fileName is missing.");
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.FileNameRequired, "fileName is required.");
        }

        var signatures = await _signatureRepository.GetByFileNameAsync(fileName, cancellationToken);
        _logger.LogInformation("Status requested for {FileName}: {SignatureCount} signature(s) found.", fileName, signatures.Count);
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
            _logger.LogInformation("Sign rejected: empty file.");
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        var baseFileName = request.FileName;
        if (!DocumentFileNameValidator.TryParse(baseFileName, out var fileNameParts))
        {
            _logger.LogInformation("Sign rejected: file name {FileName} does not match the naming convention.", baseFileName);
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
            _logger.LogInformation(
                "Sign rejected for {FileName}/{Category}: {Reason}.", baseFileName, request.Category, reason);
            return this.Error(StatusCodeFor(reason), ErrorCodeFor(reason), outcome.Message!, outcome.ErrorData);
        }

        _logger.LogInformation(
            "Signed {FileName} for category {Category} (fully signed: {FullySigned}).",
            baseFileName, request.Category, outcome.IsFullySigned);

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
            _logger.LogInformation("Verify rejected: empty file.");
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
            _logger.LogInformation("Verify: no signature found matching hash {Hash}.", hash);
            return Ok(new VerifyDocumentResponse { Found = false });
        }

        _logger.LogInformation(
            "Verify: hash matched {FileName}/{Category}, signed by {SignedBy}.",
            signature.FileName, signature.Category, signature.SignedBy);

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

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentsApi.Api.Dtos;
using DocumentsApi.Api.Errors;
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

    private static readonly Regex FileNamePattern =
        new(@"^(?<repo>[^#]+)#(?<project>[^#]+)#(?<version>[^#]+)$", RegexOptions.Compiled);

    private readonly IPdfSigningService _pdfSigningService;
    private readonly IDocumentSignatureRepository _signatureRepository;
    private readonly ISigningFailureLogger _failureLogger;
    private readonly DocumentUploadOptions _uploadOptions;
    private readonly SignaturePermissionOptions _permissionOptions;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IPdfSigningService pdfSigningService,
        IDocumentSignatureRepository signatureRepository,
        ISigningFailureLogger failureLogger,
        IOptions<DocumentUploadOptions> uploadOptions,
        IOptions<SignaturePermissionOptions> permissionOptions,
        ILogger<DocumentsController> logger)
    {
        _pdfSigningService = pdfSigningService;
        _signatureRepository = signatureRepository;
        _failureLogger = failureLogger;
        _uploadOptions = uploadOptions.Value;
        _permissionOptions = permissionOptions.Value;
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
        if (!FileNamePattern.IsMatch(baseFileName))
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

        var blankRows = SignatureCategoryExtensions.Sequence
            .Select(category => new SignatureRowInfo(category, null, null))
            .ToList();

        byte[] renderedBytes;
        try
        {
            renderedBytes = _pdfSigningService.RenderSignatureTable(sourceBytes, blankRows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare signature table for {FileName}", baseFileName);
            return this.Error(StatusCodes.Status400BadRequest, ErrorCodes.FileProcessingFailed, "The uploaded file could not be processed as a PDF.");
        }

        Response.Headers["X-File-Name"] = baseFileName;
        Response.Headers["X-Next-Expected-Category"] = SignatureCategoryExtensions.Sequence[0].ToString();

        // No Content-Disposition/file name here: this response is meant to be
        // displayed to the user (e.g. an embedded PDF viewer), not downloaded.
        return File(renderedBytes, "application/pdf");
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
        var match = FileNamePattern.Match(baseFileName);
        if (!match.Success)
        {
            return this.Error(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidFileName,
                "fileName must follow the '<RepositoryId>#<ProjectName>#<Version>' convention, " +
                "e.g. '729#VIPD2#v1.00.16' - use the value from the X-File-Name header returned by upload.");
        }

        var existing = await _signatureRepository.GetByFileNameAsync(baseFileName, cancellationToken);

        if (existing.Any(s => s.Category == request.Category))
        {
            return this.Error(
                StatusCodes.Status409Conflict,
                ErrorCodes.AlreadySigned,
                $"'{baseFileName}' has already been signed for category '{request.Category}'.",
                new { fileName = baseFileName, category = request.Category.ToString() });
        }

        var sequence = SignatureCategoryExtensions.Sequence;
        var nextExpected = sequence.First(c => existing.All(s => s.Category != c));
        if (request.Category != nextExpected)
        {
            return this.Error(
                StatusCodes.Status409Conflict,
                ErrorCodes.OutOfOrderSignature,
                $"Signatures must be applied in order. The next expected category for '{baseFileName}' is '{nextExpected}'.",
                new { fileName = baseFileName, nextExpectedCategory = nextExpected.ToString() });
        }

        var requiredRole = _permissionOptions.RoleFor(request.Category);
        if (!User.IsInRole(requiredRole))
        {
            return this.Error(
                StatusCodes.Status403Forbidden,
                ErrorCodes.RoleNotAuthorized,
                $"You don't have the '{requiredRole}' role required to sign as '{request.Category}'.",
                new { category = request.Category.ToString(), requiredRole });
        }

        byte[] currentBytes;
        using (var memoryStream = new MemoryStream())
        {
            await request.File.CopyToAsync(memoryStream, cancellationToken);
            currentBytes = memoryStream.ToArray();
        }

        // The first category has no prior stage to compare against - anything
        // that passes the checks above is accepted as the starting point.
        // Every later category must hash-match the immediately preceding
        // stage's logged result.
        var categoryIndex = sequence.ToList().IndexOf(request.Category);
        if (categoryIndex > 0)
        {
            var preceding = existing.First(s => s.Category == sequence[categoryIndex - 1]);
            var uploadedHash = Convert.ToHexString(SHA256.HashData(currentBytes));
            if (!string.Equals(uploadedHash, preceding.DocumentHash, StringComparison.OrdinalIgnoreCase))
            {
                return this.Error(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.StaleDocumentState,
                    "The uploaded file doesn't match this document's last known state. " +
                    "Re-fetch the current version (GET /api/documents/status) before signing.",
                    new { fileName = baseFileName });
            }
        }

        var signedAtUtc = DateTime.UtcNow;
        var signedBy = CurrentUserName;

        byte[] renderedBytes;
        try
        {
            renderedBytes = _pdfSigningService.FillSignatureRow(currentBytes, request.Category, signedBy, signedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sign {FileName} for category {Category}", baseFileName, request.Category);
            await _failureLogger.LogAsync(new SigningFailure
            {
                FileName = baseFileName,
                AttemptedCategory = request.Category,
                AttemptedBy = signedBy,
                OccurredAtUtc = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }, cancellationToken);
            return this.Error(StatusCodes.Status500InternalServerError, ErrorCodes.SigningFailed, "The document could not be signed due to an internal processing error.");
        }

        var documentHash = Convert.ToHexString(SHA256.HashData(renderedBytes));

        // A single insert carries everything this signing event needs
        // (category, signer, timestamp, hash) - there's no second write to
        // keep in sync, so no transaction is needed here.
        await _signatureRepository.AddAsync(new DocumentSignature
        {
            FileName = baseFileName,
            RepositoryId = match.Groups["repo"].Value,
            ProjectName = match.Groups["project"].Value,
            Version = match.Groups["version"].Value,
            Category = request.Category,
            SignedBy = signedBy,
            SignedAtUtc = signedAtUtc,
            DocumentHash = documentHash,
        }, cancellationToken);

        var isFullySigned = existing.Count + 1 >= sequence.Count;
        Response.Headers["X-Fully-Signed"] = isFullySigned.ToString();
        if (!isFullySigned)
        {
            var remainingNext = sequence.First(c => c != request.Category && existing.All(s => s.Category != c));
            Response.Headers["X-Next-Expected-Category"] = remainingNext.ToString();
        }

        return File(renderedBytes, "application/pdf", $"{baseFileName}.pdf");
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

    private string CurrentUserName =>
        User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    private static DocumentStatusResponse ToStatusResponse(string fileName, IReadOnlyList<DocumentSignature> signatures)
    {
        var isFullySigned = signatures.Count >= SignatureCategoryExtensions.Sequence.Count;
        SignatureCategory? nextExpected = isFullySigned
            ? null
            : SignatureCategoryExtensions.Sequence.First(c => signatures.All(s => s.Category != c));

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

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentsApi.Api.Dtos;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Options;
using DocumentsApi.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocumentsApi.Api.Controllers;

/// <summary>
/// This API keeps no copy of the PDF itself - only metadata and hashes are
/// persisted (via <see cref="IDocumentRepository"/>). Upload and sign both
/// return the current PDF bytes to the caller, who is responsible for
/// holding onto them (for display, and to resubmit at the next signing
/// stage). Only the signing history and hash chain live server-side.
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
    private readonly IDocumentRepository _documentRepository;
    private readonly ISigningFailureLogger _failureLogger;
    private readonly DocumentUploadOptions _uploadOptions;
    private readonly SignaturePermissionOptions _permissionOptions;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IPdfSigningService pdfSigningService,
        IDocumentRepository documentRepository,
        ISigningFailureLogger failureLogger,
        IOptions<DocumentUploadOptions> uploadOptions,
        IOptions<SignaturePermissionOptions> permissionOptions,
        ILogger<DocumentsController> logger)
    {
        _pdfSigningService = pdfSigningService;
        _documentRepository = documentRepository;
        _failureLogger = failureLogger;
        _uploadOptions = uploadOptions.Value;
        _permissionOptions = permissionOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a new document. Validates the file name convention
    /// ("&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;.pdf"), rejects an exact
    /// repeat upload (versioning must be bumped instead), stamps a blank
    /// three-row signature table onto it, and returns the result for the
    /// frontend to display - it is not saved anywhere server-side.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(RequestSizeLimitCeilingBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.File.Length == 0)
        {
            return BadRequest("The uploaded file is empty.");
        }

        if (request.File.Length > _uploadOptions.MaxFileSizeBytes)
        {
            return BadRequest($"The uploaded file exceeds the maximum allowed size of {_uploadOptions.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var isPdf = string.Equals(request.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(request.File.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf)
        {
            return BadRequest("Only PDF files are supported.");
        }

        var baseFileName = Path.GetFileNameWithoutExtension(request.File.FileName);
        var match = FileNamePattern.Match(baseFileName);
        if (!match.Success)
        {
            return BadRequest(
                "File name must follow the '<RepositoryId>#<ProjectName>#<Version>.pdf' convention, " +
                "e.g. '729#VIPD2#v1.00.16.pdf'.");
        }

        if (await _documentRepository.FileNameExistsAsync(baseFileName, cancellationToken))
        {
            return Conflict($"A document named '{baseFileName}' already exists. Bump the version to upload a new revision.");
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
            await _failureLogger.LogAsync(new SigningFailure
            {
                FileName = baseFileName,
                AttemptedBy = CurrentUserName,
                OccurredAtUtc = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }, cancellationToken);
            return BadRequest("The uploaded file could not be processed as a PDF.");
        }

        var document = new Document
        {
            FileName = baseFileName,
            RepositoryId = match.Groups["repo"].Value,
            ProjectName = match.Groups["project"].Value,
            Version = match.Groups["version"].Value,
            CreatedAtUtc = DateTime.UtcNow,
            UploadedBy = CurrentUserName,
            CurrentHash = Convert.ToHexString(SHA256.HashData(renderedBytes)),
        };
        await _documentRepository.AddAsync(document, cancellationToken);

        Response.Headers["X-Document-Id"] = document.Id.ToString();
        Response.Headers["X-Next-Expected-Category"] = SignatureCategoryExtensions.Sequence[0].ToString();

        // No Content-Disposition/file name here: this response is meant to be
        // displayed to the user (e.g. an embedded PDF viewer), not downloaded.
        return File(renderedBytes, "application/pdf");
    }

    /// <summary>
    /// Current signing status: which categories are signed, by whom, and
    /// what's expected next. Purely metadata from the database - the PDF
    /// itself isn't available here since this API doesn't store it.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetStatus(int id, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        return document is null ? NotFound() : Ok(ToStatusResponse(document));
    }

    /// <summary>
    /// Signs the document for one category. The caller must submit the PDF
    /// exactly as they currently hold it (from the upload response or a
    /// previous sign response) - it's hashed and checked against the last
    /// known state before anything is stamped, so a stale or tampered copy
    /// is rejected rather than silently signed. Categories must be signed in
    /// order (Opracował, then Sprawdził, then Zatwierdził) and the caller
    /// must hold the role mapped to the requested category. The confirmation
    /// dialog is a frontend concern; this call happens once the user confirms.
    /// The resulting PDF is returned for the user to download - it is not
    /// kept anywhere server-side.
    /// </summary>
    [HttpPost("{id:int}/sign")]
    [RequestSizeLimit(RequestSizeLimitCeilingBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Sign(int id, [FromForm] SignDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (request.File.Length == 0)
        {
            return BadRequest("The uploaded file is empty.");
        }

        if (document.Signatures.Any(s => s.Category == request.Category))
        {
            return Conflict($"This document has already been signed for category '{request.Category}'.");
        }

        var nextExpected = SignatureCategoryExtensions.Sequence
            .First(c => document.Signatures.All(s => s.Category != c));
        if (request.Category != nextExpected)
        {
            return Conflict($"Signatures must be applied in order. The next expected category is '{nextExpected}'.");
        }

        var requiredRole = _permissionOptions.RoleFor(request.Category);
        if (!User.IsInRole(requiredRole))
        {
            return Forbid();
        }

        byte[] currentBytes;
        using (var memoryStream = new MemoryStream())
        {
            await request.File.CopyToAsync(memoryStream, cancellationToken);
            currentBytes = memoryStream.ToArray();
        }

        var uploadedHash = Convert.ToHexString(SHA256.HashData(currentBytes));
        if (!string.Equals(uploadedHash, document.CurrentHash, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(
                "The uploaded file doesn't match this document's last known state. " +
                "Re-fetch the current version (GET /api/documents/{id}) before signing.");
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
            _logger.LogWarning(ex, "Failed to sign document {DocumentId} for category {Category}", id, request.Category);
            await _failureLogger.LogAsync(new SigningFailure
            {
                DocumentId = id,
                FileName = document.FileName,
                AttemptedCategory = request.Category,
                AttemptedBy = signedBy,
                OccurredAtUtc = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }, cancellationToken);
            return StatusCode(StatusCodes.Status500InternalServerError, "The document could not be signed due to an internal processing error.");
        }

        var documentHash = Convert.ToHexString(SHA256.HashData(renderedBytes));

        await _documentRepository.AddSignatureAsync(new DocumentSignature
        {
            DocumentId = id,
            Category = request.Category,
            SignedBy = signedBy,
            SignedAtUtc = signedAtUtc,
            DocumentHash = documentHash,
        }, cancellationToken);
        await _documentRepository.UpdateCurrentHashAsync(id, documentHash, cancellationToken);

        // Re-fetch rather than infer from the pre-add snapshot: EF's relationship
        // fixup already appends the just-added signature to document.Signatures
        // in-memory (same tracked DbContext), so a "+1" against that collection
        // would double-count it.
        var updated = await _documentRepository.GetByIdAsync(id, cancellationToken);
        var isFullySigned = updated!.Signatures.Count >= SignatureCategoryExtensions.Sequence.Count;
        Response.Headers["X-Document-Id"] = updated.Id.ToString();
        Response.Headers["X-Fully-Signed"] = isFullySigned.ToString();
        if (!isFullySigned)
        {
            var remainingNext = SignatureCategoryExtensions.Sequence
                .First(c => updated.Signatures.All(s => s.Category != c));
            Response.Headers["X-Next-Expected-Category"] = remainingNext.ToString();
        }

        return File(renderedBytes, "application/pdf", $"{updated.FileName}.pdf");
    }

    /// <summary>
    /// Hash-comparison tool: uploads a PDF, hashes it, and reports whether it
    /// matches a document this API has produced (and if so, at which stage).
    /// </summary>
    [HttpPost("verify")]
    [RequestSizeLimit(RequestSizeLimitCeilingBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Verify([FromForm] UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.File.Length == 0)
        {
            return BadRequest("The uploaded file is empty.");
        }

        byte[] bytes;
        using (var memoryStream = new MemoryStream())
        {
            await request.File.CopyToAsync(memoryStream, cancellationToken);
            bytes = memoryStream.ToArray();
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var match = await _documentRepository.FindByHashAsync(hash, cancellationToken);

        if (match is null)
        {
            return Ok(new VerifyDocumentResponse { Found = false });
        }

        var (document, signature) = match.Value;
        return Ok(new VerifyDocumentResponse
        {
            Found = true,
            DocumentId = document.Id,
            FileName = document.FileName,
            MatchedStage = signature?.Category.DisplayName() ?? "Uploaded (not yet signed)",
            SignedBy = signature?.SignedBy,
            SignedAtUtc = signature?.SignedAtUtc,
        });
    }

    private string CurrentUserName =>
        User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    private static DocumentStatusResponse ToStatusResponse(Document document)
    {
        var isFullySigned = document.Signatures.Count >= SignatureCategoryExtensions.Sequence.Count;
        SignatureCategory? nextExpected = isFullySigned
            ? null
            : SignatureCategoryExtensions.Sequence.First(c => document.Signatures.All(s => s.Category != c));

        return new DocumentStatusResponse
        {
            Id = document.Id,
            FileName = document.FileName,
            UploadedBy = document.UploadedBy,
            CreatedAtUtc = document.CreatedAtUtc,
            CurrentHash = document.CurrentHash,
            Signatures = document.Signatures
                .OrderBy(s => s.Category)
                .Select(s => new SignatureStatusEntry
                {
                    Category = s.Category.DisplayName(),
                    SignedBy = s.SignedBy,
                    SignedAtUtc = s.SignedAtUtc,
                })
                .ToList(),
            NextExpectedCategory = nextExpected?.DisplayName(),
            IsFullySigned = isFullySigned,
        };
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Api.Dtos;
using DocumentsApi.Core.Options;
using DocumentsApi.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocumentsApi.Api.Controllers;

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
    private readonly IDocumentFileStore _fileStore;
    private readonly ISigningFailureLogger _failureLogger;
    private readonly DocumentStorageOptions _storageOptions;
    private readonly SignaturePermissionOptions _permissionOptions;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IPdfSigningService pdfSigningService,
        IDocumentRepository documentRepository,
        IDocumentFileStore fileStore,
        ISigningFailureLogger failureLogger,
        IOptions<DocumentStorageOptions> storageOptions,
        IOptions<SignaturePermissionOptions> permissionOptions,
        ILogger<DocumentsController> logger)
    {
        _pdfSigningService = pdfSigningService;
        _documentRepository = documentRepository;
        _fileStore = fileStore;
        _failureLogger = failureLogger;
        _storageOptions = storageOptions.Value;
        _permissionOptions = permissionOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a new document. Validates the file name convention
    /// ("&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;.pdf"), rejects an exact
    /// repeat upload (versioning must be bumped instead), stamps a blank
    /// three-row signature table onto it, and stores the original bytes.
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

        if (request.File.Length > _storageOptions.MaxFileSizeBytes)
        {
            return BadRequest($"The uploaded file exceeds the maximum allowed size of {_storageOptions.MaxFileSizeBytes / (1024 * 1024)} MB.");
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
        await _fileStore.SaveOriginalAsync(document.Id, sourceBytes, cancellationToken);
        await _fileStore.SaveCurrentAsync(document.Id, renderedBytes, cancellationToken);

        return CreatedAtAction(nameof(GetStatus), new { id = document.Id }, ToStatusResponse(document));
    }

    /// <summary>
    /// Current signing status: which categories are signed, by whom, and
    /// what's expected next. Backs the "view with signing capability" screen.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetStatus(int id, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        return document is null ? NotFound() : Ok(ToStatusResponse(document));
    }

    /// <summary>
    /// Downloads the document in its current state (signature table reflects
    /// whichever categories have been signed so far).
    /// </summary>
    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> DownloadCurrent(int id, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var currentBytes = await _fileStore.ReadCurrentAsync(id, cancellationToken);
        return File(currentBytes, "application/pdf", $"{document.FileName}.pdf");
    }

    /// <summary>
    /// Signs the document for one category. Categories must be signed in
    /// order (Opracował, then Sprawdził, then Zatwierdził) and the caller
    /// must hold the role mapped to the requested category. The confirmation
    /// dialog is a frontend concern; this call happens once the user confirms.
    /// </summary>
    [HttpPost("{id:int}/sign")]
    public async Task<IActionResult> Sign(int id, [FromBody] SignDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
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

        var signedAtUtc = DateTime.UtcNow;
        var signedBy = CurrentUserName;
        var rows = BuildRows(document, request.Category, signedBy, signedAtUtc);

        byte[] renderedBytes;
        try
        {
            var originalBytes = await _fileStore.ReadOriginalAsync(id, cancellationToken);
            renderedBytes = _pdfSigningService.RenderSignatureTable(originalBytes, rows);
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
        await _fileStore.SaveCurrentAsync(id, renderedBytes, cancellationToken);

        await _documentRepository.AddSignatureAsync(new DocumentSignature
        {
            DocumentId = id,
            Category = request.Category,
            SignedBy = signedBy,
            SignedAtUtc = signedAtUtc,
            DocumentHash = documentHash,
        }, cancellationToken);
        await _documentRepository.UpdateCurrentHashAsync(id, documentHash, cancellationToken);

        var updated = await _documentRepository.GetByIdAsync(id, cancellationToken);
        return Ok(ToStatusResponse(updated!));
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

    private static List<SignatureRowInfo> BuildRows(
        Document document,
        SignatureCategory? newCategory = null,
        string? newSignedBy = null,
        DateTime? newSignedAtUtc = null)
    {
        return SignatureCategoryExtensions.Sequence.Select(category =>
        {
            if (category == newCategory)
            {
                return new SignatureRowInfo(category, newSignedBy, newSignedAtUtc);
            }

            var existing = document.Signatures.FirstOrDefault(s => s.Category == category);
            return new SignatureRowInfo(category, existing?.SignedBy, existing?.SignedAtUtc);
        }).ToList();
    }

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

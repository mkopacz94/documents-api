using System.Security.Claims;
using System.Security.Cryptography;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Pdf;
using DocumentsApi.Core.Services;
using Microsoft.Extensions.Logging;

namespace DocumentsApi.Core.Services.Signing;

public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IDocumentSignatureRepository _signatureRepository;
    private readonly ISigningWorkflowService _signingWorkflow;
    private readonly IPdfSigningService _pdfSigningService;
    private readonly ISigningFailureLogger _failureLogger;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        IDocumentSignatureRepository signatureRepository,
        ISigningWorkflowService signingWorkflow,
        IPdfSigningService pdfSigningService,
        ISigningFailureLogger failureLogger,
        ILogger<DocumentProcessingService> logger)
    {
        _signatureRepository = signatureRepository;
        _signingWorkflow = signingWorkflow;
        _pdfSigningService = pdfSigningService;
        _failureLogger = failureLogger;
        _logger = logger;
    }

    public DocumentUploadOutcome PrepareForSigning(string fileName, byte[] sourceBytes)
    {
        var blankRows = SignatureCategoryExtensions.Sequence
            .Select(category => new SignatureRowInfo(category, null, null))
            .ToList();

        try
        {
            var renderedBytes = _pdfSigningService.RenderSignatureTable(sourceBytes, blankRows);
            return DocumentUploadOutcome.Success(renderedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare signature table for {FileName}", fileName);
            return DocumentUploadOutcome.Failure("The uploaded file could not be processed as a PDF.");
        }
    }

    public async Task<SigningOutcome> SignAsync(SignDocumentCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await _signatureRepository.GetByFileNameAsync(command.FileName, cancellationToken);

        var precheck = _signingWorkflow.ValidateSigningRequest(
            command.FileName, existing, command.Category, command.User, command.SubmittedBytes);
        if (!precheck.IsValid)
        {
            return SigningOutcome.Failure(precheck);
        }

        var signedAtUtc = DateTime.UtcNow;
        var signedBy = command.User.Identity?.Name ?? command.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

        byte[] renderedBytes;
        try
        {
            renderedBytes = _pdfSigningService.FillSignatureRow(command.SubmittedBytes, command.Category, signedBy, signedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sign {FileName} for category {Category}", command.FileName, command.Category);
            await _failureLogger.LogAsync(new SigningFailure
            {
                FileName = command.FileName,
                AttemptedCategory = command.Category,
                AttemptedBy = signedBy,
                OccurredAtUtc = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }, cancellationToken);
            return SigningOutcome.Failure(
                SigningFailureReason.ProcessingFailed,
                "The document could not be signed due to an internal processing error.");
        }

        var documentHash = Convert.ToHexString(SHA256.HashData(renderedBytes));

        // A single insert carries everything this signing event needs
        // (category, signer, timestamp, hash) - there's no second write to
        // keep in sync, so no transaction is needed here.
        try
        {
            await _signatureRepository.AddAsync(new DocumentSignature
            {
                FileName = command.FileName,
                RepositoryId = command.RepositoryId,
                ProjectName = command.ProjectName,
                Version = command.Version,
                Category = command.Category,
                SignedBy = signedBy,
                SignedAtUtc = signedAtUtc,
                DocumentHash = documentHash,
            }, cancellationToken);
        }
        catch (DuplicateSignatureException ex)
        {
            // The precheck above read "not yet signed" for this category, but
            // a concurrent request won the race and persisted it first.
            _logger.LogWarning(ex, "Concurrent signing race for {FileName}/{Category}", command.FileName, command.Category);
            return SigningOutcome.Failure(SigningPrecheckResult.AlreadySigned(command.FileName, command.Category));
        }

        var isFullySigned = existing.Count + 1 >= SignatureCategoryExtensions.Sequence.Count;
        SignatureCategory? nextExpected = isFullySigned
            ? null
            : SignatureCategoryExtensions.Sequence.First(c => c != command.Category && existing.All(s => s.Category != c));

        return SigningOutcome.Success(renderedBytes, isFullySigned, nextExpected);
    }
}

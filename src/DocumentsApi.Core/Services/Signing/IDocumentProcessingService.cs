using DocumentsApi.Core.Pdf;
using DocumentsApi.Core.Services;

namespace DocumentsApi.Core.Services.Signing;

/// <summary>
/// Orchestrates both operations that touch a document's PDF bytes:
/// <see cref="PrepareForSigning"/> renders the blank signature table onto a
/// newly uploaded PDF, and <see cref="SignAsync"/> runs one signing attempt
/// end to end - the business-rule precheck (<see cref="ISigningWorkflowService"/>),
/// rendering the signed PDF (<see cref="IPdfSigningService"/>), and persisting
/// the result (<see cref="IDocumentSignatureRepository"/>). Both report every
/// failure through a single outcome type, so the API layer never needs its
/// own try/catch around either operation.
/// </summary>
public interface IDocumentProcessingService
{
    DocumentUploadOutcome PrepareForSigning(string fileName, byte[] sourceBytes);

    Task<SigningOutcome> SignAsync(SignDocumentCommand command, CancellationToken cancellationToken = default);
}

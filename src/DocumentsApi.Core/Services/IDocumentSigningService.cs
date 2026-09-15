namespace DocumentsApi.Core.Services;

/// <summary>
/// Orchestrates one signing attempt end to end: runs the business-rule
/// precheck (<see cref="ISigningWorkflowService"/>), renders the signed PDF
/// (<see cref="IPdfSigningService"/>), and persists the resulting signature
/// (<see cref="IDocumentSignatureRepository"/>) - including turning a failure
/// at any of those steps into the same <see cref="SigningOutcome"/> shape, so
/// the API layer has exactly one thing to check regardless of which step failed.
/// </summary>
public interface IDocumentSigningService
{
    Task<SigningOutcome> SignAsync(SignDocumentCommand command, CancellationToken cancellationToken = default);
}

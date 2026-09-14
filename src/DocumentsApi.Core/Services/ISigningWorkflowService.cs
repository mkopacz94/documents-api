using System.Security.Claims;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Services;

/// <summary>
/// Business rules a signing request must satisfy, separate from PDF
/// rendering (<see cref="IPdfSigningService"/>) and persistence
/// (<see cref="IDocumentSignatureRepository"/>): categories must be signed in
/// order, not twice, only by a caller holding the mapped role, and - for the
/// second and third stage - only against a file that hash-matches the
/// previously logged result.
/// </summary>
public interface ISigningWorkflowService
{
    SigningPrecheckResult ValidateSigningRequest(
        string fileName,
        IReadOnlyList<DocumentSignature> existingSignatures,
        SignatureCategory requestedCategory,
        ClaimsPrincipal user,
        byte[] submittedBytes);
}

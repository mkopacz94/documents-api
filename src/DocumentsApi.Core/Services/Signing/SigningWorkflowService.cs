using System.Security.Claims;
using System.Security.Cryptography;
using DocumentsApi.Core.Data.Entities;
using DocumentsApi.Core.Domain;
using DocumentsApi.Core.Options;
using Microsoft.Extensions.Options;

namespace DocumentsApi.Core.Services.Signing;

public class SigningWorkflowService : ISigningWorkflowService
{
    private readonly SignaturePermissionOptions _permissionOptions;

    public SigningWorkflowService(IOptions<SignaturePermissionOptions> permissionOptions)
    {
        _permissionOptions = permissionOptions.Value;
    }

    public SigningPrecheckResult ValidateSigningRequest(
        string fileName,
        IReadOnlyList<DocumentSignature> existingSignatures,
        SignatureCategory requestedCategory,
        ClaimsPrincipal user,
        byte[] submittedBytes)
    {
        if (existingSignatures.Any(s => s.Category == requestedCategory))
        {
            return SigningPrecheckResult.AlreadySigned(fileName, requestedCategory);
        }

        var nextExpected = SignatureCategoryExtensions.GetNextExpected(existingSignatures);
        if (requestedCategory != nextExpected)
        {
            return SigningPrecheckResult.Failure(
                SigningFailureReason.OutOfOrder,
                $"Signatures must be applied in order. The next expected category for '{fileName}' is '{nextExpected}'.",
                new { fileName, nextExpectedCategory = nextExpected.ToString() });
        }

        var requiredRole = _permissionOptions.RoleFor(requestedCategory);
        if (!user.IsInRole(requiredRole))
        {
            return SigningPrecheckResult.Failure(
                SigningFailureReason.RoleNotAuthorized,
                $"You don't have the '{requiredRole}' role required to sign as '{requestedCategory}'.",
                new { category = requestedCategory.ToString(), requiredRole });
        }

        // The first category has no prior stage to compare against - anything
        // that passes the checks above is accepted as the starting point.
        // Every later category must hash-match the immediately preceding
        // stage's logged result.
        var sequence = SignatureCategoryExtensions.Sequence;
        var categoryIndex = sequence.ToList().IndexOf(requestedCategory);
        if (categoryIndex > 0)
        {
            var preceding = existingSignatures.First(s => s.Category == sequence[categoryIndex - 1]);
            var uploadedHash = Convert.ToHexString(SHA256.HashData(submittedBytes));
            if (!string.Equals(uploadedHash, preceding.DocumentHash, StringComparison.OrdinalIgnoreCase))
            {
                return SigningPrecheckResult.Failure(
                    SigningFailureReason.StaleDocumentState,
                    "The uploaded file doesn't match this document's last known state. " +
                    "Re-fetch the current version (GET /api/documents/status) before signing.",
                    new { fileName });
            }
        }

        return SigningPrecheckResult.Success();
    }
}

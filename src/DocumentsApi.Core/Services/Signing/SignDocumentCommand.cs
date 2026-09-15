using System.Security.Claims;
using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Services.Signing;

/// <summary>
/// Everything <see cref="IDocumentSigningService"/> needs to attempt a single
/// signing stage. The caller has already validated the request shape (file
/// not empty, file name parses) - this only carries what's needed to run the
/// signing business rules and, if they pass, produce and persist the result.
/// </summary>
public sealed record SignDocumentCommand(
    string FileName,
    string RepositoryId,
    string ProjectName,
    string Version,
    SignatureCategory Category,
    ClaimsPrincipal User,
    byte[] SubmittedBytes);

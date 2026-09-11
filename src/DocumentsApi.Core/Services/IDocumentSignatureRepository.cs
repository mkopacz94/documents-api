using DocumentsApi.Core.Data.Entities;

namespace DocumentsApi.Core.Services;

public interface IDocumentSignatureRepository
{
    /// <summary>
    /// All signatures logged for a file name so far, ordered by category
    /// (i.e. signing order).
    /// </summary>
    Task<IReadOnlyList<DocumentSignature>> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default);

    Task AddAsync(DocumentSignature signature, CancellationToken cancellationToken = default);

    Task<DocumentSignature?> FindByHashAsync(string hash, CancellationToken cancellationToken = default);
}

using DocumentsApi.Api.Data.Entities;

namespace DocumentsApi.Api.Services;

public interface IDocumentRepository
{
    Task<bool> FileNameExistsAsync(string fileName, CancellationToken cancellationToken = default);

    Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a document together with its signatures so far.
    /// </summary>
    Task<Document?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task AddSignatureAsync(DocumentSignature signature, CancellationToken cancellationToken = default);

    Task UpdateCurrentHashAsync(int documentId, string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a hash against both per-stage signature hashes and each
    /// document's current hash (covers a freshly uploaded, not-yet-signed
    /// document). Returns the matching document and, if the match was a
    /// specific signing stage, that signature.
    /// </summary>
    Task<(Document Document, DocumentSignature? Signature)?> FindByHashAsync(string hash, CancellationToken cancellationToken = default);
}

using DocumentsApi.Api.Data.Entities;

namespace DocumentsApi.Api.Services;

public interface IDocumentSignatureRepository
{
    Task<DocumentSignature> AddAsync(DocumentSignature signature, CancellationToken cancellationToken = default);
}

using DocumentsApi.Api.Data;
using DocumentsApi.Api.Data.Entities;

namespace DocumentsApi.Api.Services;

public class DocumentSignatureRepository : IDocumentSignatureRepository
{
    private readonly AppDbContext _dbContext;

    public DocumentSignatureRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DocumentSignature> AddAsync(DocumentSignature signature, CancellationToken cancellationToken = default)
    {
        _dbContext.DocumentSignatures.Add(signature);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return signature;
    }
}

using DocumentsApi.Core.Data;
using DocumentsApi.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApi.Core.Services;

public class DocumentSignatureRepository : IDocumentSignatureRepository
{
    private readonly AppDbContext _dbContext;

    public DocumentSignatureRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DocumentSignature>> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default)
        => await _dbContext.DocumentSignatures
            .Where(s => s.FileName == fileName)
            .OrderBy(s => s.Category)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DocumentSignature signature, CancellationToken cancellationToken = default)
    {
        _dbContext.DocumentSignatures.Add(signature);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<DocumentSignature?> FindByHashAsync(string hash, CancellationToken cancellationToken = default)
        => _dbContext.DocumentSignatures.FirstOrDefaultAsync(s => s.DocumentHash == hash, cancellationToken);
}

using DocumentsApi.Api.Data;
using DocumentsApi.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentsApi.Api.Services;

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _dbContext;

    public DocumentRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> FileNameExistsAsync(string fileName, CancellationToken cancellationToken = default)
        => _dbContext.Documents.AnyAsync(d => d.FileName == fileName, cancellationToken);

    public async Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return document;
    }

    public Task<Document?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _dbContext.Documents.Include(d => d.Signatures).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task AddSignatureAsync(DocumentSignature signature, CancellationToken cancellationToken = default)
    {
        _dbContext.DocumentSignatures.Add(signature);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCurrentHashAsync(int documentId, string hash, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents.FirstAsync(d => d.Id == documentId, cancellationToken);
        document.CurrentHash = hash;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(Document Document, DocumentSignature? Signature)?> FindByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        var signature = await _dbContext.DocumentSignatures
            .Include(s => s.Document)
            .FirstOrDefaultAsync(s => s.DocumentHash == hash, cancellationToken);

        if (signature is not null)
        {
            return (signature.Document, signature);
        }

        var document = await _dbContext.Documents.FirstOrDefaultAsync(d => d.CurrentHash == hash, cancellationToken);
        return document is null ? null : (document, null);
    }
}

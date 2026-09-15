using DocumentsApi.Core.Data;
using DocumentsApi.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // The unique (FileName, Category) index rejected this insert - a
            // concurrent request signed the same category first. Surface it
            // as a distinct type so the caller can tell this apart from an
            // unexpected persistence failure.
            throw new DuplicateSignatureException(
                $"'{signature.FileName}' was already signed for category '{signature.Category}' by a concurrent request.",
                ex);
        }
    }

    public Task<DocumentSignature?> FindByHashAsync(string hash, CancellationToken cancellationToken = default)
        => _dbContext.DocumentSignatures.FirstOrDefaultAsync(s => s.DocumentHash == hash, cancellationToken);
}

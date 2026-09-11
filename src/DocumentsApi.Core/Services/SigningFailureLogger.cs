using DocumentsApi.Core.Data;
using DocumentsApi.Core.Data.Entities;

namespace DocumentsApi.Core.Services;

public class SigningFailureLogger : ISigningFailureLogger
{
    private readonly AppDbContext _dbContext;

    public SigningFailureLogger(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(SigningFailure failure, CancellationToken cancellationToken = default)
    {
        _dbContext.SigningFailures.Add(failure);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

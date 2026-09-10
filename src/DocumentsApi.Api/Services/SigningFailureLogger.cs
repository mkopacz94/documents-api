using DocumentsApi.Api.Data;
using DocumentsApi.Api.Data.Entities;

namespace DocumentsApi.Api.Services;

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

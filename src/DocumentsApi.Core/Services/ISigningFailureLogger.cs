using DocumentsApi.Core.Data.Entities;

namespace DocumentsApi.Core.Services;

public interface ISigningFailureLogger
{
    Task LogAsync(SigningFailure failure, CancellationToken cancellationToken = default);
}

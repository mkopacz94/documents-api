using DocumentsApi.Api.Data.Entities;

namespace DocumentsApi.Api.Services;

public interface ISigningFailureLogger
{
    Task LogAsync(SigningFailure failure, CancellationToken cancellationToken = default);
}

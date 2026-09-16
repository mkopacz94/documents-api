using DocumentsApi.Core.Domain;

namespace DocumentsApi.Api.Controllers;

/// <summary>
/// Outcome of <see cref="DocumentsController.SignOneAsync"/> - either the
/// rendered PDF plus signing progress, or enough to build the same
/// <see cref="Errors.ApiErrorExtensions.Error"/> response
/// <see cref="DocumentsController.Sign"/> would have returned on its own.
/// Shared so <see cref="DocumentsController.Sign"/> and
/// <see cref="DocumentsController.SignBatch"/> report identically for the
/// same failure. Internal - it carries an HTTP status code, so it belongs to
/// this controller's own request handling, not the public wire contract.
/// </summary>
internal sealed record SignAttemptResult
{
    public required string FileName { get; init; }

    public bool Success { get; private init; }

    public byte[]? RenderedBytes { get; private init; }

    public bool? IsFullySigned { get; private init; }

    public SignatureCategory? NextExpectedCategory { get; private init; }

    public int? StatusCode { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public object? ErrorData { get; private init; }

    public static SignAttemptResult SuccessResult(string fileName, byte[] renderedBytes, bool isFullySigned, SignatureCategory? nextExpectedCategory) =>
        new()
        {
            FileName = fileName,
            Success = true,
            RenderedBytes = renderedBytes,
            IsFullySigned = isFullySigned,
            NextExpectedCategory = nextExpectedCategory,
        };

    public static SignAttemptResult FailureResult(string fileName, int statusCode, string errorCode, string errorMessage, object? errorData = null) =>
        new()
        {
            FileName = fileName,
            Success = false,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ErrorData = errorData,
        };
}

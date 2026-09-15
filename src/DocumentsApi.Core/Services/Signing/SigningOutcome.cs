using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Services.Signing;

/// <summary>
/// Result of <see cref="IDocumentSigningService.SignAsync"/>: either the
/// rendered PDF plus the resulting signing progress, or the same
/// reason/message/data shape <see cref="SigningPrecheckResult"/> uses, so the
/// API layer maps failures from either source identically regardless of
/// whether the request failed a business rule or the PDF/DB step itself blew up.
/// </summary>
public sealed record SigningOutcome
{
    public bool IsValid { get; private init; }

    public SigningFailureReason? FailureReason { get; private init; }

    public string? Message { get; private init; }

    public object? ErrorData { get; private init; }

    public byte[]? RenderedBytes { get; private init; }

    public bool IsFullySigned { get; private init; }

    public SignatureCategory? NextExpectedCategory { get; private init; }

    public static SigningOutcome Success(byte[] renderedBytes, bool isFullySigned, SignatureCategory? nextExpectedCategory) =>
        new()
        {
            IsValid = true,
            RenderedBytes = renderedBytes,
            IsFullySigned = isFullySigned,
            NextExpectedCategory = nextExpectedCategory,
        };

    public static SigningOutcome Failure(SigningFailureReason reason, string message, object? errorData = null) =>
        new() { IsValid = false, FailureReason = reason, Message = message, ErrorData = errorData };

    public static SigningOutcome Failure(SigningPrecheckResult precheck) =>
        Failure(precheck.FailureReason!.Value, precheck.Message!, precheck.ErrorData);
}

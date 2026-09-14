namespace DocumentsApi.Core.Services;

/// <summary>
/// Why a signing request failed the business-rule precheck, independent of
/// how the API layer turns that into an HTTP response.
/// </summary>
public enum SigningFailureReason
{
    AlreadySigned,
    OutOfOrder,
    RoleNotAuthorized,
    StaleDocumentState,
}

/// <summary>
/// Outcome of <see cref="ISigningWorkflowService.ValidateSigningRequest"/>.
/// Carries enough structured detail (a reason, a message, and optional data)
/// for the API layer to build a ProblemDetails response without knowing the
/// underlying business rules itself.
/// </summary>
public sealed record SigningPrecheckResult
{
    public bool IsValid { get; private init; }

    public SigningFailureReason? FailureReason { get; private init; }

    public string? Message { get; private init; }

    public object? ErrorData { get; private init; }

    public static SigningPrecheckResult Success() => new() { IsValid = true };

    public static SigningPrecheckResult Failure(SigningFailureReason reason, string message, object? errorData = null) =>
        new() { IsValid = false, FailureReason = reason, Message = message, ErrorData = errorData };
}

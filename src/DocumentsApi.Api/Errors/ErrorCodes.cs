namespace DocumentsApi.Api.Errors;

/// <summary>
/// Stable, machine-readable identifiers for every error this API returns.
/// The frontend maps these to localized messages instead of parsing the
/// English text in a ProblemDetails "detail" field, which is meant as a
/// developer-facing fallback (logs, Swagger, debugging) rather than
/// something to show end users directly.
/// </summary>
public static class ErrorCodes
{
    public const string EmptyFile = "EMPTY_FILE";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string UnsupportedFileType = "UNSUPPORTED_FILE_TYPE";
    public const string InvalidFileName = "INVALID_FILE_NAME";
    public const string FileNameRequired = "FILE_NAME_REQUIRED";
    public const string FileProcessingFailed = "FILE_PROCESSING_FAILED";
    public const string AlreadySigned = "ALREADY_SIGNED";
    public const string OutOfOrderSignature = "OUT_OF_ORDER_SIGNATURE";
    public const string RoleNotAuthorized = "ROLE_NOT_AUTHORIZED";
    public const string StaleDocumentState = "STALE_DOCUMENT_STATE";
    public const string SigningFailed = "SIGNING_FAILED";
}

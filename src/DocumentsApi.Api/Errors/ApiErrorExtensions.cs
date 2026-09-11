using Microsoft.AspNetCore.Mvc;

namespace DocumentsApi.Api.Errors;

public static class ApiErrorExtensions
{
    /// <summary>
    /// Builds a ProblemDetails (RFC 7807) error response carrying a stable
    /// "errorCode" extension for the frontend to map to a localized message,
    /// alongside the English "detail" (kept as a developer-facing fallback -
    /// logs, Swagger - not meant to be shown to end users once localized).
    /// Values the frontend needs to build its own sentence (byte limits,
    /// category names, etc.) go in "errorData" as structured fields rather
    /// than embedded in the English text, since word order and pluralization
    /// differ per language. Uses the app's ProblemDetailsFactory so the
    /// response has the same shape and "application/problem+json" content
    /// type as the framework's own automatic error responses (e.g. model
    /// validation failures).
    /// </summary>
    public static ObjectResult Error(
        this ControllerBase controller,
        int statusCode,
        string errorCode,
        string detail,
        object? errorData = null)
    {
        var result = controller.Problem(detail: detail, statusCode: statusCode, title: errorCode);

        if (result.Value is ProblemDetails problemDetails)
        {
            problemDetails.Extensions["errorCode"] = errorCode;
            if (errorData is not null)
            {
                problemDetails.Extensions["errorData"] = errorData;
            }
        }

        return result;
    }
}

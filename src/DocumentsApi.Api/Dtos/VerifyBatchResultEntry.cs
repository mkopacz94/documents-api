namespace DocumentsApi.Api.Dtos;

/// <summary>
/// Per-file outcome of a batch-verify request.
/// </summary>
public class VerifyBatchResultEntry
{
    /// <summary>
    /// The uploaded file's own multipart file name - not a canonical
    /// identity (verify, unlike sign, never asks the caller for one), just
    /// enough for the caller to tell which of the files it submitted this
    /// row is about.
    /// </summary>
    public string SubmittedFileName { get; set; } = string.Empty;

    public bool Found { get; set; }

    public string? FileName { get; set; }

    /// <summary>
    /// The signature stage this hash matched.
    /// </summary>
    public string? MatchedStage { get; set; }

    public string? SignedBy { get; set; }

    public DateTime? SignedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}

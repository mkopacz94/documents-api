namespace DocumentsApi.Api.Data.Entities;

/// <summary>
/// Audit record of a user signing a PDF document.
/// </summary>
public class DocumentSignature
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string SignedBy { get; set; } = string.Empty;

    public DateTime SignedAtUtc { get; set; }

    /// <summary>
    /// SHA-256 hash (hex) of the signed PDF, so the log entry can be tied back
    /// to the exact bytes that were returned to the caller.
    /// </summary>
    public string DocumentHash { get; set; } = string.Empty;
}

using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Data.Entities;

/// <summary>
/// Audit record of a failed signing attempt (e.g. a PDF processing
/// exception), so failures are traceable even though nothing else was
/// persisted for that attempt. Upload never logs here - only an actual
/// sign attempt can fail into this table.
/// </summary>
public class SigningFailure
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public SignatureCategory AttemptedCategory { get; set; }

    public string AttemptedBy { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}

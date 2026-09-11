using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Data.Entities;

/// <summary>
/// Audit record of a failed attempt to prepare or sign a document (e.g. a PDF
/// processing exception), so failures are traceable even though nothing else
/// was persisted for that attempt.
/// </summary>
public class SigningFailure
{
    public int Id { get; set; }

    /// <summary>
    /// Null when the failure happened before the document row was created
    /// (e.g. during initial upload processing).
    /// </summary>
    public int? DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public SignatureCategory? AttemptedCategory { get; set; }

    public string AttemptedBy { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}

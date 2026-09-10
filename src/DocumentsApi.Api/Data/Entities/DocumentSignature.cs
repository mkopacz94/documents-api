using DocumentsApi.Api.Domain;

namespace DocumentsApi.Api.Data.Entities;

/// <summary>
/// Audit record of one signing stage (Opracował/Sprawdził/Zatwierdził) applied
/// to a <see cref="Document"/>.
/// </summary>
public class DocumentSignature
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public SignatureCategory Category { get; set; }

    public string SignedBy { get; set; } = string.Empty;

    public DateTime SignedAtUtc { get; set; }

    /// <summary>
    /// SHA-256 hash (hex) of the document immediately after this signature was applied.
    /// </summary>
    public string DocumentHash { get; set; } = string.Empty;
}

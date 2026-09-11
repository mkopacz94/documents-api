using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Data.Entities;

/// <summary>
/// Audit record of one signing stage (Opracował/Sprawdził/Zatwierdził) for a
/// document. There is no separate "document" record - a document's identity
/// is its file name, and its state is whatever signatures are logged
/// against that name. This is the only thing this API writes to the
/// database when a document is signed.
/// </summary>
public class DocumentSignature
{
    public int Id { get; set; }

    /// <summary>
    /// The file name without extension, following the
    /// "&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;" convention.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public SignatureCategory Category { get; set; }

    public string SignedBy { get; set; } = string.Empty;

    public DateTime SignedAtUtc { get; set; }

    /// <summary>
    /// SHA-256 hash (hex) of the document immediately after this signature was applied.
    /// </summary>
    public string DocumentHash { get; set; } = string.Empty;
}

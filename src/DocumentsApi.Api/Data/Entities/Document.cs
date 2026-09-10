namespace DocumentsApi.Api.Data.Entities;

/// <summary>
/// An uploaded document tracked through its multi-stage signing lifecycle.
/// The original PDF bytes are kept immutable in the file store; the signature
/// table is re-rendered on top of them from <see cref="Signatures"/> whenever
/// the current state is needed, so this row plus its signatures is the single
/// source of truth for what the document currently looks like.
/// </summary>
public class Document
{
    public int Id { get; set; }

    /// <summary>
    /// The uploaded file name without extension, following the
    /// "&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;" convention. Unique per document
    /// so re-uploading the same repository/project/version is rejected and a
    /// new version must be used instead.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash (hex) of the document's current rendered state (including
    /// whichever signatures have been applied so far).
    /// </summary>
    public string CurrentHash { get; set; } = string.Empty;

    public List<DocumentSignature> Signatures { get; set; } = new();
}

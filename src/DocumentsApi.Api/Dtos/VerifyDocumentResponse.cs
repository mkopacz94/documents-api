namespace DocumentsApi.Api.Dtos;

public class VerifyDocumentResponse
{
    public bool Found { get; set; }

    public int? DocumentId { get; set; }

    public string? FileName { get; set; }

    /// <summary>
    /// The signature stage this hash matched, or "Uploaded (not yet signed)"
    /// when it matches a document's initial, blank-table state.
    /// </summary>
    public string? MatchedStage { get; set; }

    public string? SignedBy { get; set; }

    public DateTime? SignedAtUtc { get; set; }
}

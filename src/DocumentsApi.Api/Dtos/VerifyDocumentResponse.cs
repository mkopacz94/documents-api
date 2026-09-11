namespace DocumentsApi.Api.Dtos;

public class VerifyDocumentResponse
{
    public bool Found { get; set; }

    public string? FileName { get; set; }

    /// <summary>
    /// The signature stage this hash matched.
    /// </summary>
    public string? MatchedStage { get; set; }

    public string? SignedBy { get; set; }

    public DateTime? SignedAtUtc { get; set; }
}

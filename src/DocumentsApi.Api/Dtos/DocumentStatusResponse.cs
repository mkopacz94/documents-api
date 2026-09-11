namespace DocumentsApi.Api.Dtos;

public class DocumentStatusResponse
{
    public string FileName { get; set; } = string.Empty;

    public List<SignatureStatusEntry> Signatures { get; set; } = new();

    /// <summary>
    /// Hash of the most recently signed stage, or null if nothing has been
    /// signed for this file name yet.
    /// </summary>
    public string? CurrentHash { get; set; }

    public string? NextExpectedCategory { get; set; }

    public bool IsFullySigned { get; set; }
}

public class SignatureStatusEntry
{
    public string Category { get; set; } = string.Empty;

    public string SignedBy { get; set; } = string.Empty;

    public DateTime SignedAtUtc { get; set; }
}

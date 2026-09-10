namespace DocumentsApi.Api.Dtos;

public class DocumentStatusResponse
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string UploadedBy { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public string CurrentHash { get; set; } = string.Empty;

    public List<SignatureStatusEntry> Signatures { get; set; } = new();

    public string? NextExpectedCategory { get; set; }

    public bool IsFullySigned { get; set; }
}

public class SignatureStatusEntry
{
    public string Category { get; set; } = string.Empty;

    public string SignedBy { get; set; } = string.Empty;

    public DateTime SignedAtUtc { get; set; }
}

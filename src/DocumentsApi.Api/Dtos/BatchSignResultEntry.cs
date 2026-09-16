namespace DocumentsApi.Api.Dtos;

/// <summary>
/// Per-file outcome of a batch-sign request, written into "results.json"
/// inside the response zip alongside the successfully signed PDFs (a failed
/// file has no corresponding zip entry - only a row here explaining why).
/// </summary>
public class BatchSignResultEntry
{
    public string FileName { get; set; } = string.Empty;

    public bool Success { get; set; }

    public bool? IsFullySigned { get; set; }

    public string? NextExpectedCategory { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public object? ErrorData { get; set; }
}

using System.ComponentModel.DataAnnotations;
using DocumentsApi.Core.Domain;

namespace DocumentsApi.Api.Dtos;

/// <summary>
/// One entry of a batch-sign request - same fields as <see cref="SignDocumentRequest"/>,
/// just repeated per file. Bound from multipart form fields named
/// "Files[0].File", "Files[0].FileName", "Files[0].Category", "Files[1]...", etc.
/// </summary>
public class SignDocumentItemRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;

    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public SignatureCategory Category { get; set; }
}

public class SignDocumentsBatchRequest
{
    [Required]
    public List<SignDocumentItemRequest> Files { get; set; } = [];
}

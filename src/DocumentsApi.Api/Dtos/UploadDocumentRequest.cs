using System.ComponentModel.DataAnnotations;

namespace DocumentsApi.Api.Dtos;

public class UploadDocumentRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}

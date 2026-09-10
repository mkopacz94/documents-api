using System.ComponentModel.DataAnnotations;

namespace DocumentsApi.Api.Dtos;

public class SignDocumentRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Username { get; set; } = string.Empty;
}

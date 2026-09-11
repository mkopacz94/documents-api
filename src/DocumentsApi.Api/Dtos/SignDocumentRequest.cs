using System.ComponentModel.DataAnnotations;
using DocumentsApi.Core.Domain;

namespace DocumentsApi.Api.Dtos;

public class SignDocumentRequest
{
    /// <summary>
    /// The PDF exactly as the caller currently holds it - i.e. whatever this
    /// API returned from the upload call or the previous sign call. The API
    /// keeps no copy of its own, so this is the only source of the document's
    /// current state.
    /// </summary>
    [Required]
    public IFormFile File { get; set; } = null!;

    /// <summary>
    /// The document's identity (the "&lt;RepositoryId&gt;#&lt;ProjectName&gt;#&lt;Version&gt;"
    /// name, without extension) exactly as returned by upload in the
    /// X-File-Name header. Deliberately a separate field rather than reusing
    /// <see cref="File"/>'s own multipart file name: a browser round-trips
    /// the PDF as an unnamed Blob (fetch().blob() carries no name at all), so
    /// relying on that metadata for identity is fragile - this field is
    /// explicit instead.
    /// </summary>
    [Required]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Which of the three signature stages this request signs. The signer's
    /// identity comes from the authenticated caller, never from this body.
    /// </summary>
    [Required]
    public SignatureCategory Category { get; set; }
}

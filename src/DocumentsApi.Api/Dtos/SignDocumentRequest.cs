using System.ComponentModel.DataAnnotations;
using DocumentsApi.Core.Domain;

namespace DocumentsApi.Api.Dtos;

public class SignDocumentRequest
{
    /// <summary>
    /// Which of the three signature stages this request signs. The signer's
    /// identity comes from the authenticated caller, never from this body.
    /// </summary>
    [Required]
    public SignatureCategory Category { get; set; }
}

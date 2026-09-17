using System.ComponentModel.DataAnnotations;

namespace DocumentsApi.Api.Dtos;

/// <summary>
/// Unlike sign, verify never needs a canonical fileName or category - it
/// just hashes whatever bytes it's given and looks up a match - so a batch
/// is just a list of files. Bound from repeated "Files" multipart fields
/// (all sharing that same key, e.g. two form parts both named "Files"), not
/// an indexed "Files[0].File" pattern: a per-item wrapper type with only an
/// IFormFile property and nothing else doesn't reliably bind that way,
/// unlike sign/batch's items (which also carry FileName/Category).
/// </summary>
public class VerifyDocumentsBatchRequest
{
    [Required]
    public List<IFormFile> Files { get; set; } = [];
}

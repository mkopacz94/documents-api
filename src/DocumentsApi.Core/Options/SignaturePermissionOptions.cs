using DocumentsApi.Core.Domain;

namespace DocumentsApi.Core.Options;

/// <summary>
/// Maps each signature category to the role/group name that the external
/// identity provider issues for it, so this API doesn't hardcode the IdP's
/// naming convention. Bound from the "SignaturePermissions" configuration section.
/// </summary>
public class SignaturePermissionOptions
{
    public const string SectionName = "SignaturePermissions";

    public string Opracowal { get; set; } = "DocumentSigner.Opracowal";

    public string Sprawdzil { get; set; } = "DocumentSigner.Sprawdzil";

    public string Zatwierdzil { get; set; } = "DocumentSigner.Zatwierdzil";

    public string RoleFor(SignatureCategory category) => category switch
    {
        SignatureCategory.Opracowal => Opracowal,
        SignatureCategory.Sprawdzil => Sprawdzil,
        SignatureCategory.Zatwierdzil => Zatwierdzil,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };
}

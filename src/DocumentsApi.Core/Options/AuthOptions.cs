using System.Security.Claims;

namespace DocumentsApi.Core.Options;

/// <summary>
/// Bound from the "Auth" configuration section. This API trusts an external
/// identity provider - it does not manage users or credentials itself.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// OIDC authority of the external identity provider
    /// (e.g. "https://login.microsoftonline.com/{tenant}/v2.0", or an
    /// on-prem ADFS/Keycloak issuer URL). Leave empty in Development to fall
    /// back to header-based dev authentication - see DevHeaderAuthenticationHandler.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Expected "aud" claim value for tokens issued to this API.
    /// </summary>
    public string? Audience { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Claim type the identity provider uses to carry group/role membership.
    /// </summary>
    public string RoleClaimType { get; set; } = ClaimTypes.Role;

    /// <summary>
    /// Claim type the identity provider uses to carry the signer's display name.
    /// </summary>
    public string NameClaimType { get; set; } = ClaimTypes.Name;
}

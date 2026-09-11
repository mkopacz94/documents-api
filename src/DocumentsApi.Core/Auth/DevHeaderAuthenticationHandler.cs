using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentsApi.Core.Auth;

/// <summary>
/// Development-only stand-in for the real external identity provider. Trusts
/// the caller-supplied "X-Dev-User" and "X-Dev-Roles" headers, so the API can
/// be exercised locally without standing up a real OIDC provider. Never wired
/// up outside the Development environment - see Program.cs.
/// </summary>
public class DevHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevHeader";

    private const string UserHeader = "X-Dev-User";
    private const string RolesHeader = "X-Dev-Roles";

    public DevHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userValues) || string.IsNullOrWhiteSpace(userValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var roles = Request.Headers.TryGetValue(RolesHeader, out var roleValues)
            ? roleValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        var claims = new List<Claim> { new(ClaimTypes.Name, userValues.ToString()) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

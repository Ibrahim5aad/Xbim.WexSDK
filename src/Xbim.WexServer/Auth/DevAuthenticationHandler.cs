using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Xbim.WexServer.Auth;

/// <summary>
/// Authentication handler that injects a fixed development principal.
/// Used for local development without requiring an external identity provider.
/// </summary>
public class DevAuthenticationHandler : AuthenticationHandler<DevAuthenticationOptions>
{
    public const string SchemeName = "DevAuth";
    public const string DefaultSubject = "dev-user";
    public const string DefaultEmail = "dev@localhost";
    public const string DefaultDisplayName = "Development User";

    public DevAuthenticationHandler(
        IOptionsMonitor<DevAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Options.Subject ?? DefaultSubject),
            new("sub", Options.Subject ?? DefaultSubject),
            new(ClaimTypes.Email, Options.Email ?? DefaultEmail),
            new("email", Options.Email ?? DefaultEmail),
            new(ClaimTypes.Name, Options.DisplayName ?? DefaultDisplayName),
            new("name", Options.DisplayName ?? DefaultDisplayName)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        Logger.LogDebug("DevAuth: Authenticated as {Subject}", Options.Subject ?? DefaultSubject);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Options for the development authentication handler.
/// </summary>
public class DevAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The subject (sub) claim value for the development user.
    /// Defaults to "dev-user".
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// The email claim value for the development user.
    /// Defaults to "dev@localhost".
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// The display name for the development user.
    /// Defaults to "Development User".
    /// </summary>
    public string? DisplayName { get; set; }
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;

using DomainAuditEventType = Octopus.Server.Domain.Enums.PersonalAccessTokenAuditEventType;

namespace Octopus.Server.App.Auth;

/// <summary>
/// Authentication handler for Personal Access Tokens.
/// PATs are identified by the "ocpat_" prefix in the Bearer token.
/// </summary>
public class PersonalAccessTokenAuthenticationHandler : AuthenticationHandler<PersonalAccessTokenAuthenticationOptions>
{
    public const string SchemeName = "PersonalAccessToken";
    public const string TokenPrefix = "ocpat_";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOAuthTokenService _tokenService;

    public PersonalAccessTokenAuthenticationHandler(
        IOptionsMonitor<PersonalAccessTokenAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServiceScopeFactory scopeFactory,
        IOAuthTokenService tokenService)
        : base(options, logger, encoder)
    {
        _scopeFactory = scopeFactory;
        _tokenService = tokenService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for Authorization header
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader))
        {
            return AuthenticateResult.NoResult();
        }

        // Check for Bearer scheme
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader["Bearer ".Length..].Trim();

        // Check if it's a PAT (starts with "ocpat_")
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            // Not a PAT, let other handlers deal with it
            return AuthenticateResult.NoResult();
        }

        // Validate the PAT
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

            var tokenHash = _tokenService.HashPersonalAccessToken(token);

            var pat = await dbContext.PersonalAccessTokens
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.TokenHash == tokenHash);

            if (pat == null)
            {
                Logger.LogWarning("PAT authentication failed: Token not found");
                return AuthenticateResult.Fail("Invalid personal access token.");
            }

            // Check if revoked
            if (pat.IsRevoked)
            {
                Logger.LogWarning("PAT authentication failed: Token {TokenId} is revoked", pat.Id);
                return AuthenticateResult.Fail("Personal access token has been revoked.");
            }

            // Check if expired
            if (pat.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Logger.LogWarning("PAT authentication failed: Token {TokenId} is expired", pat.Id);
                return AuthenticateResult.Fail("Personal access token has expired.");
            }

            // Token is valid - update LastUsedAt and create audit log
            var clientIp = GetClientIpAddress();
            var userAgent = Request.Headers.UserAgent.FirstOrDefault();

            pat.LastUsedAt = DateTimeOffset.UtcNow;
            pat.LastUsedIpAddress = clientIp;

            // Only log usage periodically (not on every request) to reduce database load
            // Log if never used before OR if last audit log for this token was more than 1 hour ago
            var shouldLogUsage = Options.LogEveryUsage;
            if (!shouldLogUsage)
            {
                var lastUsageLog = await dbContext.PersonalAccessTokenAuditLogs
                    .Where(l => l.PersonalAccessTokenId == pat.Id && l.EventType == DomainAuditEventType.Used)
                    .OrderByDescending(l => l.Timestamp)
                    .FirstOrDefaultAsync();

                shouldLogUsage = lastUsageLog == null ||
                    lastUsageLog.Timestamp < DateTimeOffset.UtcNow.AddHours(-1);
            }

            if (shouldLogUsage)
            {
                var auditLog = new PersonalAccessTokenAuditLog
                {
                    Id = Guid.NewGuid(),
                    PersonalAccessTokenId = pat.Id,
                    EventType = DomainAuditEventType.Used,
                    ActorUserId = pat.UserId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Details = JsonSerializer.Serialize(new
                    {
                        Path = Request.Path.ToString(),
                        Method = Request.Method
                    }),
                    IpAddress = clientIp,
                    UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent
                };
                dbContext.PersonalAccessTokenAuditLogs.Add(auditLog);
            }

            await dbContext.SaveChangesAsync();

            // Build claims principal
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, pat.User?.Subject ?? pat.UserId.ToString()),
                new("sub", pat.User?.Subject ?? pat.UserId.ToString()),
                new("user_id", pat.UserId.ToString()),
                new("tid", pat.WorkspaceId.ToString()), // Tenant/workspace ID
                new("pat_id", pat.Id.ToString()),
                new("scp", pat.Scopes), // Scopes
                new("token_type", "pat") // Indicate this is a PAT-authenticated request
            };

            if (pat.User != null)
            {
                if (!string.IsNullOrEmpty(pat.User.Email))
                {
                    claims.Add(new Claim(ClaimTypes.Email, pat.User.Email));
                    claims.Add(new Claim("email", pat.User.Email));
                }
                if (!string.IsNullOrEmpty(pat.User.DisplayName))
                {
                    claims.Add(new Claim(ClaimTypes.Name, pat.User.DisplayName));
                    claims.Add(new Claim("name", pat.User.DisplayName));
                }
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            Logger.LogDebug("PAT authentication successful: User {UserId} via PAT {TokenId}",
                pat.UserId, pat.Id);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error validating personal access token");
            return AuthenticateResult.Fail("Error validating personal access token.");
        }
    }

    private string? GetClientIpAddress()
    {
        // Try X-Forwarded-For first (for reverse proxies)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP if there are multiple
            return forwardedFor.Split(',')[0].Trim();
        }

        return Context.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>
/// Options for the Personal Access Token authentication handler.
/// </summary>
public class PersonalAccessTokenAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Whether to log every PAT usage. Default is false (logs once per hour per token).
    /// Set to true for high-security environments.
    /// </summary>
    public bool LogEveryUsage { get; set; } = false;
}

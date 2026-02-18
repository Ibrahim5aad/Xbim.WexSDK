namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents an OAuth 2.0 authorization code issued during the authorization flow.
/// Codes are single-use and expire after a short time (typically 10 minutes).
/// </summary>
public class AuthorizationCode
{
    public Guid Id { get; set; }

    /// <summary>
    /// Hashed authorization code value.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// The OAuth app (client) that requested this code.
    /// </summary>
    public Guid OAuthAppId { get; set; }

    /// <summary>
    /// The user who authorized this code.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The workspace context for this authorization (used for tid claim).
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Space-separated list of granted scopes.
    /// </summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>
    /// The redirect URI used for this authorization request.
    /// Must match exactly when exchanging the code.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// PKCE code challenge (base64url-encoded).
    /// </summary>
    public string? CodeChallenge { get; set; }

    /// <summary>
    /// PKCE code challenge method (S256 or plain).
    /// </summary>
    public string? CodeChallengeMethod { get; set; }

    /// <summary>
    /// When the code was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the code expires. Typically 10 minutes after creation.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Whether the code has been used. Codes are single-use.
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// When the code was used (if used).
    /// </summary>
    public DateTimeOffset? UsedAt { get; set; }

    // Navigation properties
    public OAuthApp? OAuthApp { get; set; }
    public User? User { get; set; }
    public Workspace? Workspace { get; set; }
}

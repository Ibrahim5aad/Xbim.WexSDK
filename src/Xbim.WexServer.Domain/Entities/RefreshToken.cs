namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents an OAuth refresh token with rotation and revocation support.
/// Refresh tokens are hashed for storage and rotated on each use.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 hash of the token value for secure storage and lookup.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The OAuth app that this token was issued to.
    /// </summary>
    public Guid OAuthAppId { get; set; }

    /// <summary>
    /// The user who authorized this token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The workspace context for this token.
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Space-separated list of scopes granted to this token.
    /// </summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>
    /// When the token was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the token expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Whether the token has been revoked.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When the token was revoked (if applicable).
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Reason for revocation (e.g., "user_logout", "token_rotation", "admin_revoked", "token_reuse_detected").
    /// </summary>
    public string? RevokedReason { get; set; }

    /// <summary>
    /// The parent token that this token replaced (for rotation chain tracking).
    /// When a token is used, a new token is issued and this field links back to the original.
    /// </summary>
    public Guid? ParentTokenId { get; set; }

    /// <summary>
    /// The token that replaced this one when it was rotated.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>
    /// Token family identifier for detecting token reuse attacks.
    /// All tokens in a rotation chain share the same family ID.
    /// </summary>
    public Guid TokenFamilyId { get; set; }

    /// <summary>
    /// IP address from which the token was issued (for audit purposes).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent from which the token was issued (for audit purposes).
    /// </summary>
    public string? UserAgent { get; set; }

    // Navigation properties
    public OAuthApp? OAuthApp { get; set; }
    public User? User { get; set; }
    public Workspace? Workspace { get; set; }
    public RefreshToken? ParentToken { get; set; }
    public RefreshToken? ReplacedByToken { get; set; }
}

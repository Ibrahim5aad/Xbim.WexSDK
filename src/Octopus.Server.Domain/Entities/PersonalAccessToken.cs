namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents a Personal Access Token (PAT) for API automation and server-side integrations.
/// PATs are workspace-scoped, carry specific scopes, and are designed for non-browser usage.
/// </summary>
public class PersonalAccessToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 hash of the token value for secure storage and lookup.
    /// The actual token is shown only once at creation time.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// First 8 characters of the token for identification purposes (e.g., "ocpat_Ab").
    /// This allows users to identify which token is which without exposing the full value.
    /// </summary>
    public string TokenPrefix { get; set; } = string.Empty;

    /// <summary>
    /// The user who owns this token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The workspace this token is scoped to.
    /// PATs are always workspace-scoped for multi-tenant isolation.
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// User-friendly name for the token (e.g., "CI/CD Pipeline", "Local Development").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this token is used for.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Space-separated list of scopes granted to this token.
    /// </summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>
    /// When the token was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the token expires. PATs should have an expiration for security.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// When the token was last used to authenticate an API request.
    /// Null if never used.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// IP address from which the token was last used.
    /// </summary>
    public string? LastUsedIpAddress { get; set; }

    /// <summary>
    /// Whether the token has been revoked.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When the token was revoked (if applicable).
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Reason for revocation (e.g., "user_revoked", "admin_revoked", "security_concern").
    /// </summary>
    public string? RevokedReason { get; set; }

    /// <summary>
    /// IP address from which the token was created (for audit purposes).
    /// </summary>
    public string? CreatedFromIpAddress { get; set; }

    /// <summary>
    /// Whether the token is currently valid (not revoked and not expired).
    /// </summary>
    public bool IsActive => !IsRevoked && ExpiresAt > DateTimeOffset.UtcNow;

    // Navigation properties
    public User? User { get; set; }
    public Workspace? Workspace { get; set; }
}

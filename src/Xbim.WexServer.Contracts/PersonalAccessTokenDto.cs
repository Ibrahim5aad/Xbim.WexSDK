namespace Xbim.WexServer.Contracts;

/// <summary>
/// PAT audit event type.
/// </summary>
public enum PersonalAccessTokenAuditEventType
{
    Created = 0,
    Used = 1,
    RevokedByUser = 2,
    RevokedByAdmin = 3,
    Expired = 4,
    Updated = 5
}

/// <summary>
/// Personal Access Token details.
/// </summary>
public record PersonalAccessTokenDto
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid WorkspaceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>
    /// The first characters of the token (e.g., "ocpat_Ab...") for identification.
    /// </summary>
    public string TokenPrefix { get; init; } = string.Empty;

    /// <summary>
    /// Space-separated scopes granted to this token.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public string? LastUsedIpAddress { get; init; }
    public bool IsRevoked { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>
    /// Whether the token is currently usable (not revoked and not expired).
    /// </summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// Response when creating a new PAT, includes the plain-text token (shown only once).
/// </summary>
public record PersonalAccessTokenCreatedDto
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid WorkspaceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>
    /// The full token value in plain text. Only returned on creation.
    /// Store this securely - it cannot be retrieved again.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// The first characters of the token for identification.
    /// </summary>
    public string TokenPrefix { get; init; } = string.Empty;

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Request to create a new Personal Access Token.
/// </summary>
public record CreatePersonalAccessTokenRequest
{
    /// <summary>
    /// User-friendly name for the token (e.g., "CI/CD Pipeline", "Local Development").
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional description of what this token is used for.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Scopes to grant to this token.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Number of days until the token expires. Must be between 1 and 365.
    /// </summary>
    public int ExpiresInDays { get; init; } = 90;
}

/// <summary>
/// Request to update an existing Personal Access Token.
/// Only name and description can be updated.
/// </summary>
public record UpdatePersonalAccessTokenRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// PAT audit log entry.
/// </summary>
public record PersonalAccessTokenAuditLogDto
{
    public Guid Id { get; init; }
    public Guid PersonalAccessTokenId { get; init; }
    public PersonalAccessTokenAuditEventType EventType { get; init; }
    public Guid ActorUserId { get; init; }
    public string? ActorUserName { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

/// <summary>
/// Summary of PATs for a workspace (admin view).
/// </summary>
public record WorkspacePersonalAccessTokenSummaryDto
{
    public Guid TokenId { get; init; }
    public Guid UserId { get; init; }
    public string UserDisplayName { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public string TokenName { get; init; } = string.Empty;
    public string TokenPrefix { get; init; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool IsActive { get; init; }
}

namespace Xbim.WexServer.Contracts;

/// <summary>
/// OAuth client type.
/// </summary>
public enum OAuthClientType
{
    /// <summary>
    /// Public clients cannot securely store credentials (SPAs, mobile apps, desktop apps).
    /// </summary>
    Public = 0,

    /// <summary>
    /// Confidential clients can securely store credentials (server-side apps).
    /// </summary>
    Confidential = 1
}

/// <summary>
/// OAuth app audit event type.
/// </summary>
public enum OAuthAppAuditEventType
{
    Created = 0,
    Updated = 1,
    Enabled = 2,
    Disabled = 3,
    Deleted = 4,
    SecretRotated = 5,
    RefreshTokenIssued = 6,
    RefreshTokenRevoked = 7,
    AllRefreshTokensRevoked = 8,
    TokenReuseDetected = 9
}

/// <summary>
/// OAuth application details.
/// </summary>
public record OAuthAppDto
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public OAuthClientType ClientType { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public IReadOnlyList<string> RedirectUris { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowedScopes { get; init; } = Array.Empty<string>();
    public bool IsEnabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public Guid CreatedByUserId { get; init; }
}

/// <summary>
/// Response when creating a new OAuth app, includes the plain-text client secret (shown only once).
/// </summary>
public record OAuthAppCreatedDto
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public OAuthClientType ClientType { get; init; }
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// The client secret in plain text. Only returned on creation and secret rotation.
    /// Null for public clients.
    /// </summary>
    public string? ClientSecret { get; init; }

    public IReadOnlyList<string> RedirectUris { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowedScopes { get; init; } = Array.Empty<string>();
    public bool IsEnabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Response when rotating a client secret.
/// </summary>
public record OAuthAppSecretRotatedDto
{
    public Guid AppId { get; init; }
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// The new client secret in plain text. Shown only once.
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    public DateTimeOffset RotatedAt { get; init; }
}

/// <summary>
/// Request to create a new OAuth app.
/// </summary>
public record CreateOAuthAppRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public OAuthClientType ClientType { get; init; }
    public IReadOnlyList<string> RedirectUris { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowedScopes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Request to update an existing OAuth app.
/// </summary>
public record UpdateOAuthAppRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? RedirectUris { get; init; }
    public IReadOnlyList<string>? AllowedScopes { get; init; }
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// OAuth app audit log entry.
/// </summary>
public record OAuthAppAuditLogDto
{
    public Guid Id { get; init; }
    public Guid OAuthAppId { get; init; }
    public OAuthAppAuditEventType EventType { get; init; }
    public Guid ActorUserId { get; init; }
    public string? ActorUserName { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
}

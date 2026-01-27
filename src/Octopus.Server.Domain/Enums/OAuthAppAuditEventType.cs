namespace Octopus.Server.Domain.Enums;

/// <summary>
/// Types of audit events for OAuth applications.
/// </summary>
public enum OAuthAppAuditEventType
{
    /// <summary>
    /// App was created.
    /// </summary>
    Created = 0,

    /// <summary>
    /// App properties were updated.
    /// </summary>
    Updated = 1,

    /// <summary>
    /// App was enabled.
    /// </summary>
    Enabled = 2,

    /// <summary>
    /// App was disabled.
    /// </summary>
    Disabled = 3,

    /// <summary>
    /// App was deleted.
    /// </summary>
    Deleted = 4,

    /// <summary>
    /// Client secret was rotated.
    /// </summary>
    SecretRotated = 5,

    /// <summary>
    /// A refresh token was issued.
    /// </summary>
    RefreshTokenIssued = 6,

    /// <summary>
    /// A refresh token was revoked by user or admin.
    /// </summary>
    RefreshTokenRevoked = 7,

    /// <summary>
    /// All refresh tokens for the app were revoked.
    /// </summary>
    AllRefreshTokensRevoked = 8,

    /// <summary>
    /// Token reuse was detected (security event).
    /// </summary>
    TokenReuseDetected = 9
}

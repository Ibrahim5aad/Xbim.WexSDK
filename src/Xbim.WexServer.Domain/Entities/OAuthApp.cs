using Xbim.WexServer.Domain.Enums;

namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents a workspace-scoped OAuth application (client) registration.
/// </summary>
public class OAuthApp
{
    public Guid Id { get; set; }

    /// <summary>
    /// The workspace this app belongs to.
    /// </summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Human-readable name for the application.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the application.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is a public or confidential client.
    /// </summary>
    public OAuthClientType ClientType { get; set; }

    /// <summary>
    /// The OAuth client_id (publicly visible identifier).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Hashed client secret for confidential clients. Null for public clients.
    /// </summary>
    public string? ClientSecretHash { get; set; }

    /// <summary>
    /// JSON array of allowed redirect URIs.
    /// </summary>
    public string RedirectUris { get; set; } = "[]";

    /// <summary>
    /// Space-separated list of allowed OAuth scopes.
    /// </summary>
    public string AllowedScopes { get; set; } = string.Empty;

    /// <summary>
    /// Whether the app is enabled. Disabled apps cannot authenticate.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When the app was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the app was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// User who created the app.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<OAuthAppAuditLog> AuditLogs { get; set; } = new List<OAuthAppAuditLog>();
}

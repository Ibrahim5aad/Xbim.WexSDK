using Xbim.WexServer.Domain.Enums;

namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Audit log entry for OAuth application events.
/// </summary>
public class OAuthAppAuditLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// The OAuth app this event relates to.
    /// </summary>
    public Guid OAuthAppId { get; set; }

    /// <summary>
    /// Type of the audit event.
    /// </summary>
    public OAuthAppAuditEventType EventType { get; set; }

    /// <summary>
    /// User who performed the action.
    /// </summary>
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Optional JSON details about the change (e.g., which fields were modified).
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// IP address of the actor (if available).
    /// </summary>
    public string? IpAddress { get; set; }

    // Navigation properties
    public OAuthApp? OAuthApp { get; set; }
    public User? ActorUser { get; set; }
}

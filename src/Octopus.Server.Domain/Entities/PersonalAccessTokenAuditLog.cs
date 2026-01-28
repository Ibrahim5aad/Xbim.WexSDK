using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Audit log entry for Personal Access Token events.
/// </summary>
public class PersonalAccessTokenAuditLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// The PAT this event relates to.
    /// </summary>
    public Guid PersonalAccessTokenId { get; set; }

    /// <summary>
    /// Type of the audit event.
    /// </summary>
    public PersonalAccessTokenAuditEventType EventType { get; set; }

    /// <summary>
    /// User who performed the action.
    /// For 'Used' events, this is the same as the PAT owner.
    /// For 'RevokedByAdmin' events, this is the admin who revoked.
    /// </summary>
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Optional JSON details about the event (e.g., endpoint accessed, changes made).
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// IP address of the actor (if available).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string (for 'Used' events).
    /// </summary>
    public string? UserAgent { get; set; }

    // Navigation properties
    public PersonalAccessToken? PersonalAccessToken { get; set; }
    public User? ActorUser { get; set; }
}

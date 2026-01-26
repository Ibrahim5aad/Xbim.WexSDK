using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents a user's membership in a workspace.
/// </summary>
public class WorkspaceMembership
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}

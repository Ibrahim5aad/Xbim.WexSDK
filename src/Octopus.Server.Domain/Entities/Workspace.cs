namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents a workspace - the top-level organizational unit.
/// </summary>
public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<WorkspaceMembership> Memberships { get; set; } = new List<WorkspaceMembership>();
    public ICollection<WorkspaceInvite> Invites { get; set; } = new List<WorkspaceInvite>();
}

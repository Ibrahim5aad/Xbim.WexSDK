using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents a user's membership in a project.
/// </summary>
public class ProjectMembership
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public ProjectRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public Project? Project { get; set; }
    public User? User { get; set; }
}

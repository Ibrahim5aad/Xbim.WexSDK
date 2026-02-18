namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents a user (external subject or local identity).
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    // Navigation properties
    public ICollection<WorkspaceMembership> WorkspaceMemberships { get; set; } = new List<WorkspaceMembership>();
    public ICollection<ProjectMembership> ProjectMemberships { get; set; } = new List<ProjectMembership>();
}

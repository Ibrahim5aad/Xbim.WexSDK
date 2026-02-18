namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents a project within a workspace.
/// </summary>
public class Project
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public ICollection<ProjectMembership> Memberships { get; set; } = new List<ProjectMembership>();
    public ICollection<FileEntity> Files { get; set; } = new List<FileEntity>();
    public ICollection<Model> Models { get; set; } = new List<Model>();
    public ICollection<UploadSession> UploadSessions { get; set; } = new List<UploadSession>();
}

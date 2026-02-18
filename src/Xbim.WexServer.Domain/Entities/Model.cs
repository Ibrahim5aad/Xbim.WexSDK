namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents a BIM model.
/// </summary>
public class Model
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation properties
    public Project? Project { get; set; }
    public ICollection<ModelVersion> Versions { get; set; } = new List<ModelVersion>();
}

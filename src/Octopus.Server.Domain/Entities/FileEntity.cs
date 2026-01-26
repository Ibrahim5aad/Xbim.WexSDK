using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents a file in the registry.
/// Named FileEntity to avoid conflict with System.IO.File.
/// </summary>
public class FileEntity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? Checksum { get; set; }
    public FileKind Kind { get; set; }
    public FileCategory Category { get; set; }
    public string StorageProvider { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation properties
    public Project? Project { get; set; }
    public ICollection<FileLink> SourceLinks { get; set; } = new List<FileLink>();
    public ICollection<FileLink> TargetLinks { get; set; } = new List<FileLink>();
}

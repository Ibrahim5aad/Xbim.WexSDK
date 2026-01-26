using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents a version of a BIM model.
/// </summary>
public class ModelVersion
{
    public Guid Id { get; set; }
    public Guid ModelId { get; set; }
    public int VersionNumber { get; set; }
    public Guid IfcFileId { get; set; }
    public Guid? WexBimFileId { get; set; }
    public Guid? PropertiesFileId { get; set; }
    public ProcessingStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    // Navigation properties
    public Model? Model { get; set; }
    public FileEntity? IfcFile { get; set; }
    public FileEntity? WexBimFile { get; set; }
    public FileEntity? PropertiesFile { get; set; }
}

namespace Xbim.WexServer.Contracts;

/// <summary>
/// Processing status for a model version.
/// </summary>
public enum ProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3
}

/// <summary>
/// Represents a version of a BIM model.
/// </summary>
public record ModelVersionDto
{
    public Guid Id { get; init; }
    public Guid ModelId { get; init; }
    public int VersionNumber { get; init; }
    public Guid IfcFileId { get; init; }
    public Guid? WexBimFileId { get; init; }
    public Guid? PropertiesFileId { get; init; }
    public ProcessingStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}

public record CreateModelVersionRequest
{
    public Guid IfcFileId { get; init; }
}

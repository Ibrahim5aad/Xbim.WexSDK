namespace Xbim.WexServer.Contracts;

/// <summary>
/// Storage usage statistics for a workspace.
/// </summary>
public record WorkspaceUsageDto
{
    public Guid WorkspaceId { get; init; }
    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
    public long? QuotaBytes { get; init; }
    public DateTimeOffset CalculatedAt { get; init; }
}

/// <summary>
/// Storage usage statistics for a project.
/// </summary>
public record ProjectUsageDto
{
    public Guid ProjectId { get; init; }
    public Guid WorkspaceId { get; init; }
    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
    public DateTimeOffset CalculatedAt { get; init; }
}

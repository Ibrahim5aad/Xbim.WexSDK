namespace Xbim.WexServer.Contracts;

/// <summary>
/// Represents a BIM model.
/// </summary>
public record ModelDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public record CreateModelRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public record UpdateModelRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
}

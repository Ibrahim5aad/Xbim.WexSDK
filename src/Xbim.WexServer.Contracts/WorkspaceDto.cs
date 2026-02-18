namespace Xbim.WexServer.Contracts;

/// <summary>
/// Represents a workspace - the top-level organizational unit.
/// </summary>
public record WorkspaceDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public record CreateWorkspaceRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public record UpdateWorkspaceRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
}

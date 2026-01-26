namespace Octopus.Server.Contracts;

/// <summary>
/// Workspace membership roles.
/// </summary>
public enum WorkspaceRole
{
    Guest = 0,
    Member = 1,
    Admin = 2,
    Owner = 3
}

/// <summary>
/// Project membership roles.
/// </summary>
public enum ProjectRole
{
    Viewer = 0,
    Editor = 1,
    ProjectAdmin = 2
}

/// <summary>
/// Represents a user's membership in a workspace.
/// </summary>
public record WorkspaceMembershipDto
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public Guid UserId { get; init; }
    public WorkspaceRole Role { get; init; }
    public UserDto? User { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Represents a user's membership in a project.
/// </summary>
public record ProjectMembershipDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid UserId { get; init; }
    public ProjectRole Role { get; init; }
    public UserDto? User { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record InviteUserRequest
{
    public string Email { get; init; } = string.Empty;
    public WorkspaceRole Role { get; init; }
}

public record UpdateMembershipRequest
{
    public WorkspaceRole? WorkspaceRole { get; init; }
    public ProjectRole? ProjectRole { get; init; }
}

/// <summary>
/// Request to update a workspace member's role.
/// </summary>
public record UpdateWorkspaceMemberRequest
{
    public WorkspaceRole Role { get; init; }
}

/// <summary>
/// Represents a workspace invitation.
/// </summary>
public record WorkspaceInviteDto
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public string Email { get; init; } = string.Empty;
    public WorkspaceRole Role { get; init; }
    public string Token { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsAccepted { get; init; }
    public DateTimeOffset? AcceptedAt { get; init; }
}

/// <summary>
/// Request to create a workspace invitation.
/// </summary>
public record CreateWorkspaceInviteRequest
{
    public string Email { get; init; } = string.Empty;
    public WorkspaceRole Role { get; init; } = WorkspaceRole.Member;
}

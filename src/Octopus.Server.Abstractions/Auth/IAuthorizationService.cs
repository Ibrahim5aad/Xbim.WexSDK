using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Abstractions.Auth;

/// <summary>
/// Authorization service for checking user permissions.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks if the current user has access to a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="minimumRole">The minimum role required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if authorized.</returns>
    Task<bool> CanAccessWorkspaceAsync(Guid workspaceId, WorkspaceRole minimumRole = WorkspaceRole.Guest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user has access to a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="minimumRole">The minimum role required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if authorized.</returns>
    Task<bool> CanAccessProjectAsync(Guid projectId, ProjectRole minimumRole = ProjectRole.Viewer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user's role in a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's role, or null if no membership.</returns>
    Task<WorkspaceRole?> GetWorkspaceRoleAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user's role in a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's role, or null if no membership.</returns>
    Task<ProjectRole?> GetProjectRoleAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the current user has access to a workspace, throwing if not authorized.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="minimumRole">The minimum role required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown if not authorized.</exception>
    Task RequireWorkspaceAccessAsync(Guid workspaceId, WorkspaceRole minimumRole = WorkspaceRole.Guest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the current user has access to a project, throwing if not authorized.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="minimumRole">The minimum role required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">Thrown if not authorized.</exception>
    Task RequireProjectAccessAsync(Guid projectId, ProjectRole minimumRole = ProjectRole.Viewer, CancellationToken cancellationToken = default);
}

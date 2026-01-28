using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.Domain.Enums;
using Octopus.Server.Persistence.EfCore;

namespace Octopus.Server.App.Auth;

/// <summary>
/// Implementation of the authorization service that checks user permissions
/// against workspace and project memberships in the database and OAuth scopes.
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly IUserContext _userContext;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly OctopusDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private HashSet<string>? _cachedScopes;

    public AuthorizationService(
        IUserContext userContext,
        IWorkspaceContext workspaceContext,
        OctopusDbContext dbContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _userContext = userContext;
        _workspaceContext = workspaceContext;
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    #region Scope-based Authorization

    /// <inheritdoc />
    public IReadOnlySet<string> GetScopes()
    {
        if (_cachedScopes != null)
        {
            return _cachedScopes;
        }

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
        {
            _cachedScopes = new HashSet<string>();
            return _cachedScopes;
        }

        // The "scp" claim contains space-separated scopes
        var scopeClaim = user.FindFirst("scp")?.Value;
        if (string.IsNullOrEmpty(scopeClaim))
        {
            _cachedScopes = new HashSet<string>();
            return _cachedScopes;
        }

        _cachedScopes = scopeClaim
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        return _cachedScopes;
    }

    /// <inheritdoc />
    public bool HasScope(string scope)
    {
        return GetScopes().Contains(scope);
    }

    /// <inheritdoc />
    public bool HasAnyScope(params string[] scopes)
    {
        var presentScopes = GetScopes();
        return scopes.Any(s => presentScopes.Contains(s));
    }

    /// <inheritdoc />
    public bool HasAllScopes(params string[] scopes)
    {
        var presentScopes = GetScopes();
        return scopes.All(s => presentScopes.Contains(s));
    }

    /// <inheritdoc />
    public void RequireScope(params string[] scopes)
    {
        if (scopes.Length == 0)
        {
            return;
        }

        if (!_userContext.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        var presentScopes = GetScopes();

        // If no scopes are present in the token, it means the token wasn't obtained via OAuth/PAT
        // (e.g., development mode or direct OIDC without scope claims).
        // In this case, we allow access for backward compatibility.
        if (presentScopes.Count == 0)
        {
            return;
        }

        if (!scopes.Any(s => presentScopes.Contains(s)))
        {
            throw new InsufficientScopeException(scopes, presentScopes);
        }
    }

    /// <inheritdoc />
    public void RequireAllScopes(params string[] scopes)
    {
        if (scopes.Length == 0)
        {
            return;
        }

        if (!_userContext.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        var presentScopes = GetScopes();

        // If no scopes are present in the token, allow access for backward compatibility
        if (presentScopes.Count == 0)
        {
            return;
        }

        var missingScopes = scopes.Where(s => !presentScopes.Contains(s)).ToList();
        if (missingScopes.Count > 0)
        {
            throw new InsufficientScopeException(scopes, presentScopes);
        }
    }

    #endregion

    #region Role-based Authorization (RBAC)

    /// <inheritdoc />
    public async Task<bool> CanAccessWorkspaceAsync(
        Guid workspaceId,
        WorkspaceRole minimumRole = WorkspaceRole.Guest,
        CancellationToken cancellationToken = default)
    {
        var role = await GetWorkspaceRoleAsync(workspaceId, cancellationToken);
        return role.HasValue && role.Value >= minimumRole;
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessProjectAsync(
        Guid projectId,
        ProjectRole minimumRole = ProjectRole.Viewer,
        CancellationToken cancellationToken = default)
    {
        var role = await GetProjectRoleAsync(projectId, cancellationToken);
        return role.HasValue && role.Value >= minimumRole;
    }

    /// <inheritdoc />
    public async Task<WorkspaceRole?> GetWorkspaceRoleAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (!_userContext.IsAuthenticated || !_userContext.UserId.HasValue)
        {
            return null;
        }

        var membership = await _dbContext.WorkspaceMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.WorkspaceId == workspaceId && m.UserId == _userContext.UserId.Value,
                cancellationToken);

        return membership?.Role;
    }

    /// <inheritdoc />
    public async Task<ProjectRole?> GetProjectRoleAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (!_userContext.IsAuthenticated || !_userContext.UserId.HasValue)
        {
            return null;
        }

        var userId = _userContext.UserId.Value;

        // First check direct project membership
        var projectMembership = await _dbContext.ProjectMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.ProjectId == projectId && m.UserId == userId,
                cancellationToken);

        if (projectMembership != null)
        {
            return projectMembership.Role;
        }

        // Check workspace-level access (Admin/Owner get ProjectAdmin access to all projects in workspace)
        var project = await _dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return null;
        }

        var workspaceMembership = await _dbContext.WorkspaceMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.WorkspaceId == project.WorkspaceId && m.UserId == userId,
                cancellationToken);

        if (workspaceMembership == null)
        {
            return null;
        }

        // Workspace Admin and Owner get ProjectAdmin access to all projects
        if (workspaceMembership.Role >= WorkspaceRole.Admin)
        {
            return ProjectRole.ProjectAdmin;
        }

        // Workspace Member gets Viewer access to all projects
        if (workspaceMembership.Role >= WorkspaceRole.Member)
        {
            return ProjectRole.Viewer;
        }

        // Workspace Guest has no implicit project access
        return null;
    }

    /// <inheritdoc />
    public async Task RequireWorkspaceAccessAsync(
        Guid workspaceId,
        WorkspaceRole minimumRole = WorkspaceRole.Guest,
        CancellationToken cancellationToken = default)
    {
        if (!_userContext.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        if (!await CanAccessWorkspaceAsync(workspaceId, minimumRole, cancellationToken))
        {
            throw new ForbiddenAccessException(
                $"Access denied. Minimum workspace role required: {minimumRole}");
        }
    }

    /// <inheritdoc />
    public async Task RequireProjectAccessAsync(
        Guid projectId,
        ProjectRole minimumRole = ProjectRole.Viewer,
        CancellationToken cancellationToken = default)
    {
        if (!_userContext.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        if (!await CanAccessProjectAsync(projectId, minimumRole, cancellationToken))
        {
            throw new ForbiddenAccessException(
                $"Access denied. Minimum project role required: {minimumRole}");
        }
    }

    #endregion

    #region Workspace Isolation (Multi-tenant)

    /// <inheritdoc />
    public Guid? GetBoundWorkspaceId()
    {
        return _workspaceContext.WorkspaceId;
    }

    /// <inheritdoc />
    public bool ValidateWorkspaceIsolation(Guid workspaceId)
    {
        // If no workspace context is bound (e.g., dev auth mode), isolation is not enforced
        if (!_workspaceContext.IsBound)
        {
            return true;
        }

        // Check if the workspace matches the token's bound workspace
        return _workspaceContext.WorkspaceId == workspaceId;
    }

    /// <inheritdoc />
    public void RequireWorkspaceIsolation(Guid workspaceId)
    {
        // If no workspace context is bound (e.g., dev auth mode), skip isolation check
        if (!_workspaceContext.IsBound)
        {
            return;
        }

        var boundWorkspaceId = _workspaceContext.WorkspaceId!.Value;
        if (boundWorkspaceId != workspaceId)
        {
            throw new WorkspaceIsolationException(boundWorkspaceId, workspaceId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ValidateProjectWorkspaceIsolationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // If no workspace context is bound, isolation is not enforced
        if (!_workspaceContext.IsBound)
        {
            return true;
        }

        // Look up the project's workspace
        var project = await _dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            // Project doesn't exist - return true to let the endpoint handle 404
            return true;
        }

        return _workspaceContext.WorkspaceId == project.WorkspaceId;
    }

    /// <inheritdoc />
    public async Task RequireProjectWorkspaceIsolationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // If no workspace context is bound, skip isolation check
        if (!_workspaceContext.IsBound)
        {
            return;
        }

        // Look up the project's workspace
        var project = await _dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            // Project doesn't exist - let the endpoint handle 404
            return;
        }

        var boundWorkspaceId = _workspaceContext.WorkspaceId!.Value;
        if (boundWorkspaceId != project.WorkspaceId)
        {
            throw new WorkspaceIsolationException(boundWorkspaceId, project.WorkspaceId);
        }
    }

    #endregion
}

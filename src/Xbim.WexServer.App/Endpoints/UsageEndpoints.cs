using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Persistence.EfCore;

using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using ProjectRole = Xbim.WexServer.Domain.Enums.ProjectRole;
using static Xbim.WexServer.Abstractions.Auth.OAuthScopes;

namespace Xbim.WexServer.App.Endpoints;

/// <summary>
/// Storage usage API endpoints.
/// </summary>
public static class UsageEndpoints
{
    /// <summary>
    /// Maps usage-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder app)
    {
        // Workspace usage endpoint
        app.MapGet("/api/v1/workspaces/{workspaceId:guid}/usage", GetWorkspaceUsage)
            .WithTags("Usage")
            .WithName("GetWorkspaceUsage")
            .WithOpenApi()
            .RequireAuthorization();

        // Project usage endpoint
        app.MapGet("/api/v1/projects/{projectId:guid}/usage", GetProjectUsage)
            .WithTags("Usage")
            .WithName("GetProjectUsage")
            .WithOpenApi()
            .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Gets storage usage statistics for a workspace.
    /// Sums SizeBytes for all non-deleted files across all projects in the workspace.
    /// Requires at least Guest role in the workspace.
    /// Requires scope: workspaces:read
    /// </summary>
    private static async Task<IResult> GetWorkspaceUsage(
        Guid workspaceId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require workspaces:read scope
        authZ.RequireScope(WorkspacesRead);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Check workspace access - any membership role is sufficient to view usage
        var role = await authZ.GetWorkspaceRoleAsync(workspaceId, cancellationToken);
        if (!role.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found or access denied." });
        }

        // Verify workspace exists
        var workspaceExists = await dbContext.Workspaces
            .AnyAsync(w => w.Id == workspaceId, cancellationToken);

        if (!workspaceExists)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found." });
        }

        // Get all project IDs in this workspace
        var projectIds = await dbContext.Projects
            .Where(p => p.WorkspaceId == workspaceId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        // Aggregate file usage across all projects in the workspace
        // Only count non-deleted files
        var usageStats = await dbContext.Files
            .Where(f => projectIds.Contains(f.ProjectId) && !f.IsDeleted)
            .GroupBy(_ => 1) // Group all into one
            .Select(g => new
            {
                TotalBytes = g.Sum(f => f.SizeBytes),
                FileCount = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var usage = new WorkspaceUsageDto
        {
            WorkspaceId = workspaceId,
            TotalBytes = usageStats?.TotalBytes ?? 0,
            FileCount = usageStats?.FileCount ?? 0,
            QuotaBytes = null, // Quota not implemented yet
            CalculatedAt = DateTimeOffset.UtcNow
        };

        return Results.Ok(usage);
    }

    /// <summary>
    /// Gets storage usage statistics for a project.
    /// Sums SizeBytes for all non-deleted files in the project.
    /// Requires at least Viewer role in the project.
    /// Requires scope: projects:read
    /// </summary>
    private static async Task<IResult> GetProjectUsage(
        Guid projectId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require projects:read scope
        authZ.RequireScope(ProjectsRead);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Check project access - Viewer role is sufficient to view usage
        if (!await authZ.CanAccessProjectAsync(projectId, ProjectRole.Viewer, cancellationToken))
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found or access denied." });
        }

        // Get the project to verify it exists and get workspace ID
        var project = await dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.WorkspaceId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found." });
        }

        // Aggregate file usage for this project
        // Only count non-deleted files
        var usageStats = await dbContext.Files
            .Where(f => f.ProjectId == projectId && !f.IsDeleted)
            .GroupBy(_ => 1) // Group all into one
            .Select(g => new
            {
                TotalBytes = g.Sum(f => f.SizeBytes),
                FileCount = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var usage = new ProjectUsageDto
        {
            ProjectId = projectId,
            WorkspaceId = project.WorkspaceId,
            TotalBytes = usageStats?.TotalBytes ?? 0,
            FileCount = usageStats?.FileCount ?? 0,
            CalculatedAt = DateTimeOffset.UtcNow
        };

        return Results.Ok(usage);
    }
}

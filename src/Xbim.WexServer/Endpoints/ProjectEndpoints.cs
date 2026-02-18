using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using ProjectRole = Xbim.WexServer.Domain.Enums.ProjectRole;
using static Xbim.WexServer.Abstractions.Auth.OAuthScopes;

namespace Xbim.WexServer.Endpoints;

/// <summary>
/// Project API endpoints.
/// </summary>
public static class ProjectEndpoints
{
    /// <summary>
    /// Maps project-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        // Workspace-scoped endpoints for creating and listing projects
        var workspaceGroup = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        workspaceGroup.MapPost("", CreateProject)
            .WithName("CreateProject")
            .Produces<ProjectDto>(StatusCodes.Status201Created)
            .WithOpenApi();

        workspaceGroup.MapGet("", ListProjects)
            .WithName("ListProjects")
            .Produces<PagedList<ProjectDto>>()
            .WithOpenApi();

        // Direct project endpoints for get/update
        var projectGroup = app.MapGroup("/api/v1/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        projectGroup.MapGet("/{projectId:guid}", GetProject)
            .WithName("GetProject")
            .Produces<ProjectDto>()
            .WithOpenApi();

        projectGroup.MapPut("/{projectId:guid}", UpdateProject)
            .WithName("UpdateProject")
            .Produces<ProjectDto>()
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Creates a new project in a workspace. Requires Member role or higher in the workspace.
    /// Requires scope: projects:write
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> CreateProject(
        Guid workspaceId,
        CreateProjectRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require projects:write scope
        authZ.RequireScope(ProjectsWrite);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require at least Member role to create projects in a workspace
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Member, cancellationToken);

        // Validate the workspace exists
        var workspaceExists = await dbContext.Workspaces
            .AnyAsync(w => w.Id == workspaceId, cancellationToken);

        if (!workspaceExists)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Validation Error", message = "Name is required." });
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = MapToDto(project);
        return Results.Created($"/api/v1/projects/{project.Id}", dto);
    }

    /// <summary>
    /// Lists all projects in a workspace that the user has access to.
    /// Requires scope: projects:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> ListProjects(
        Guid workspaceId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require projects:read scope
        authZ.RequireScope(ProjectsRead);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Check workspace access first (any role is sufficient to list projects)
        var workspaceRole = await authZ.GetWorkspaceRoleAsync(workspaceId, cancellationToken);
        if (!workspaceRole.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found or access denied." });
        }

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var userId = userContext.UserId.Value;

        // Query for projects in the workspace
        IQueryable<Project> query;

        // Workspace Admins and Owners can see all projects in the workspace
        if (workspaceRole.Value >= WorkspaceRole.Admin)
        {
            query = dbContext.Projects
                .Where(p => p.WorkspaceId == workspaceId);
        }
        // Workspace Members can see all projects (they have implicit Viewer access)
        else if (workspaceRole.Value >= WorkspaceRole.Member)
        {
            query = dbContext.Projects
                .Where(p => p.WorkspaceId == workspaceId);
        }
        // Workspace Guests can only see projects they have direct membership to
        else
        {
            var directProjectIds = dbContext.ProjectMemberships
                .Where(pm => pm.UserId == userId && pm.Project!.WorkspaceId == workspaceId)
                .Select(pm => pm.ProjectId);

            query = dbContext.Projects
                .Where(p => p.WorkspaceId == workspaceId && directProjectIds.Contains(p.Id));
        }

        query = query.OrderByDescending(p => p.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(p => MapToDto(p))
            .ToListAsync(cancellationToken);

        var result = new PagedList<ProjectDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Gets a project by ID. Requires any project access.
    /// Requires scope: projects:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> GetProject(
        Guid projectId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require projects:read scope
        authZ.RequireScope(ProjectsRead);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Check access (any project role is sufficient to view)
        var role = await authZ.GetProjectRoleAsync(projectId, cancellationToken);
        if (!role.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found or access denied." });
        }

        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found." });
        }

        return Results.Ok(MapToDto(project));
    }

    /// <summary>
    /// Updates a project. Requires ProjectAdmin role or higher.
    /// Requires scope: projects:write
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> UpdateProject(
        Guid projectId,
        UpdateProjectRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require projects:write scope
        authZ.RequireScope(ProjectsWrite);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require ProjectAdmin role to update project
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.ProjectAdmin, cancellationToken);

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found." });
        }

        // Update fields if provided
        var updated = false;

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Validation Error", message = "Name cannot be empty." });
            }
            project.Name = request.Name.Trim();
            updated = true;
        }

        if (request.Description != null)
        {
            project.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            updated = true;
        }

        if (updated)
        {
            project.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(MapToDto(project));
    }

    private static ProjectDto MapToDto(Project project)
    {
        return new ProjectDto
        {
            Id = project.Id,
            WorkspaceId = project.WorkspaceId,
            Name = project.Name,
            Description = project.Description,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }
}

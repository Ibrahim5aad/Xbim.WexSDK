using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using static Xbim.WexServer.Abstractions.Auth.OAuthScopes;

namespace Xbim.WexServer.App.Endpoints;

/// <summary>
/// Workspace API endpoints.
/// </summary>
public static class WorkspaceEndpoints
{
    /// <summary>
    /// Maps workspace-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces")
            .WithTags("Workspaces")
            .RequireAuthorization();

        group.MapPost("", CreateWorkspace)
            .WithName("CreateWorkspace")
            .Produces<WorkspaceDto>(StatusCodes.Status201Created)
            .WithOpenApi();

        group.MapGet("", ListWorkspaces)
            .WithName("ListWorkspaces")
            .Produces<PagedList<WorkspaceDto>>()
            .WithOpenApi();

        group.MapGet("/{workspaceId:guid}", GetWorkspace)
            .WithName("GetWorkspace")
            .Produces<WorkspaceDto>()
            .WithOpenApi();

        group.MapPut("/{workspaceId:guid}", UpdateWorkspace)
            .WithName("UpdateWorkspace")
            .Produces<WorkspaceDto>()
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Creates a new workspace and makes the current user the Owner.
    /// Requires scope: workspaces:write
    /// </summary>
    private static async Task<IResult> CreateWorkspace(
        CreateWorkspaceRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require workspaces:write scope
        authZ.RequireScope(WorkspacesWrite);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Validation Error", message = "Name is required." });
        }

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Create the workspace
        dbContext.Workspaces.Add(workspace);

        // Make the current user the Owner
        var membership = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = userContext.UserId.Value,
            Role = WorkspaceRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.WorkspaceMemberships.Add(membership);

        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = MapToDto(workspace);
        return Results.Created($"/api/v1/workspaces/{workspace.Id}", dto);
    }

    /// <summary>
    /// Lists all workspaces the current user is a member of.
    /// When token has tid claim, only returns that workspace (if user is a member).
    /// Requires scope: workspaces:read
    /// </summary>
    private static async Task<IResult> ListWorkspaces(
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

        // Require workspaces:read scope
        authZ.RequireScope(WorkspacesRead);

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Get workspace IDs where the user is a member
        var memberWorkspaceIds = dbContext.WorkspaceMemberships
            .Where(m => m.UserId == userContext.UserId.Value)
            .Select(m => m.WorkspaceId);

        // Query workspaces - filter by bound workspace if token has tid claim
        var boundWorkspaceId = authZ.GetBoundWorkspaceId();
        var query = dbContext.Workspaces
            .Where(w => memberWorkspaceIds.Contains(w.Id))
            .Where(w => !boundWorkspaceId.HasValue || w.Id == boundWorkspaceId.Value)
            .OrderByDescending(w => w.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(w => MapToDto(w))
            .ToListAsync(cancellationToken);

        var result = new PagedList<WorkspaceDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Gets a workspace by ID. Requires the user to be a member.
    /// Requires scope: workspaces:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> GetWorkspace(
        Guid workspaceId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require workspaces:read scope
        authZ.RequireScope(WorkspacesRead);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Check access (any membership role is sufficient to view)
        var role = await authZ.GetWorkspaceRoleAsync(workspaceId, cancellationToken);
        if (!role.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found or access denied." });
        }

        var workspace = await dbContext.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found." });
        }

        return Results.Ok(MapToDto(workspace));
    }

    /// <summary>
    /// Updates a workspace. Requires Admin role or higher.
    /// Requires scope: workspaces:write
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> UpdateWorkspace(
        Guid workspaceId,
        UpdateWorkspaceRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require workspaces:write scope
        authZ.RequireScope(WorkspacesWrite);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to update workspace
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        var workspace = await dbContext.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found." });
        }

        // Update fields if provided
        var updated = false;

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Validation Error", message = "Name cannot be empty." });
            }
            workspace.Name = request.Name.Trim();
            updated = true;
        }

        if (request.Description != null)
        {
            workspace.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            updated = true;
        }

        if (updated)
        {
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(MapToDto(workspace));
    }

    private static WorkspaceDto MapToDto(Workspace workspace)
    {
        return new WorkspaceDto
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Description = workspace.Description,
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt
        };
    }
}

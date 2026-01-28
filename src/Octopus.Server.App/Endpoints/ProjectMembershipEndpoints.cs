using Microsoft.EntityFrameworkCore;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.Contracts;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;

using ProjectRole = Octopus.Server.Domain.Enums.ProjectRole;
using WorkspaceRole = Octopus.Server.Domain.Enums.WorkspaceRole;
using ContractProjectRole = Octopus.Server.Contracts.ProjectRole;

namespace Octopus.Server.App.Endpoints;

/// <summary>
/// Project membership API endpoints.
/// </summary>
public static class ProjectMembershipEndpoints
{
    /// <summary>
    /// Maps project membership-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapProjectMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/projects/{projectId:guid}/members")
            .WithTags("Project Memberships")
            .RequireAuthorization();

        // List members
        group.MapGet("", ListMembers)
            .WithName("ListProjectMembers")
            .WithOpenApi();

        // Add member
        group.MapPost("", AddMember)
            .WithName("AddProjectMember")
            .WithOpenApi();

        // Update member role
        group.MapPut("/{membershipId:guid}", UpdateMemberRole)
            .WithName("UpdateProjectMemberRole")
            .WithOpenApi();

        // Remove member
        group.MapDelete("/{membershipId:guid}", RemoveMember)
            .WithName("RemoveProjectMember")
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Lists all members of a project.
    /// </summary>
    private static async Task<IResult> ListMembers(
        Guid projectId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Check access (any project role is sufficient to view members)
        var role = await authZ.GetProjectRoleAsync(projectId, cancellationToken);
        if (!role.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found or access denied." });
        }

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.ProjectMemberships
            .Where(m => m.ProjectId == projectId)
            .Include(m => m.User)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(m => MapToDto(m))
            .ToListAsync(cancellationToken);

        var result = new PagedList<ProjectMembershipDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Adds a member to a project. The user must already have workspace access.
    /// </summary>
    private static async Task<IResult> AddMember(
        Guid projectId,
        AddProjectMemberRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require ProjectAdmin role to add members
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.ProjectAdmin, cancellationToken);

        // Get the project to find its workspace
        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found." });
        }

        // Find the user by ID
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "User not found." });
        }

        // Verify the user has workspace access (is a member of the workspace)
        var workspaceMembership = await dbContext.WorkspaceMemberships
            .AnyAsync(wm => wm.WorkspaceId == project.WorkspaceId && wm.UserId == request.UserId, cancellationToken);

        if (!workspaceMembership)
        {
            return Results.BadRequest(new { error = "Validation Error", message = "User must be a member of the workspace to be added to the project." });
        }

        // Check if user is already a project member
        var existingMembership = await dbContext.ProjectMemberships
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == request.UserId, cancellationToken);

        if (existingMembership)
        {
            return Results.Conflict(new { error = "Conflict", message = "User is already a member of this project." });
        }

        var domainRole = MapToDomainRole(request.Role);

        var membership = new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UserId = request.UserId,
            Role = domainRole,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ProjectMemberships.Add(membership);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Load user for response
        membership.User = user;

        return Results.Created($"/api/v1/projects/{projectId}/members/{membership.Id}", MapToDto(membership));
    }

    /// <summary>
    /// Updates a project member's role.
    /// </summary>
    private static async Task<IResult> UpdateMemberRole(
        Guid projectId,
        Guid membershipId,
        UpdateProjectMemberRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Check user's role (need to verify they have ProjectAdmin access)
        var currentUserRole = await authZ.GetProjectRoleAsync(projectId, cancellationToken);
        if (!currentUserRole.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found or access denied." });
        }

        if (currentUserRole.Value < ProjectRole.ProjectAdmin)
        {
            return Results.Forbid();
        }

        // Find the membership to update
        var membership = await dbContext.ProjectMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.ProjectId == projectId, cancellationToken);

        if (membership == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Membership not found." });
        }

        // Cannot change your own role
        if (membership.UserId == userContext.UserId.Value)
        {
            return Results.BadRequest(new { error = "Invalid Operation", message = "You cannot change your own role." });
        }

        var newRole = MapToDomainRole(request.Role);
        membership.Role = newRole;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(MapToDto(membership));
    }

    /// <summary>
    /// Removes a member from the project.
    /// </summary>
    private static async Task<IResult> RemoveMember(
        Guid projectId,
        Guid membershipId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Check user's role
        var currentUserRole = await authZ.GetProjectRoleAsync(projectId, cancellationToken);
        if (!currentUserRole.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found or access denied." });
        }

        // Find the membership to remove
        var membership = await dbContext.ProjectMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.ProjectId == projectId, cancellationToken);

        if (membership == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Membership not found." });
        }

        var isSelf = membership.UserId == userContext.UserId.Value;

        if (isSelf)
        {
            // User can leave the project (remove themselves)
            // This is allowed for any member
        }
        else
        {
            // Removing others requires ProjectAdmin role
            if (currentUserRole.Value < ProjectRole.ProjectAdmin)
            {
                return Results.Forbid();
            }
        }

        dbContext.ProjectMemberships.Remove(membership);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static ProjectMembershipDto MapToDto(ProjectMembership membership)
    {
        return new ProjectMembershipDto
        {
            Id = membership.Id,
            ProjectId = membership.ProjectId,
            UserId = membership.UserId,
            Role = MapToContractRole(membership.Role),
            User = membership.User != null ? new UserDto
            {
                Id = membership.User.Id,
                Subject = membership.User.Subject,
                Email = membership.User.Email,
                DisplayName = membership.User.DisplayName,
                CreatedAt = membership.User.CreatedAt,
                LastLoginAt = membership.User.LastLoginAt
            } : null,
            CreatedAt = membership.CreatedAt
        };
    }

    private static ContractProjectRole MapToContractRole(ProjectRole role)
    {
        return role switch
        {
            ProjectRole.Viewer => ContractProjectRole.Viewer,
            ProjectRole.Editor => ContractProjectRole.Editor,
            ProjectRole.ProjectAdmin => ContractProjectRole.ProjectAdmin,
            _ => ContractProjectRole.Viewer
        };
    }

    private static ProjectRole MapToDomainRole(ContractProjectRole role)
    {
        return role switch
        {
            ContractProjectRole.Viewer => ProjectRole.Viewer,
            ContractProjectRole.Editor => ProjectRole.Editor,
            ContractProjectRole.ProjectAdmin => ProjectRole.ProjectAdmin,
            _ => ProjectRole.Viewer
        };
    }
}

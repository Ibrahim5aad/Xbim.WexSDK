using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using ContractWorkspaceRole = Xbim.WexServer.Contracts.WorkspaceRole;

namespace Xbim.WexServer.App.Endpoints;

/// <summary>
/// Workspace membership API endpoints.
/// </summary>
public static class WorkspaceMembershipEndpoints
{
    /// <summary>
    /// Maps workspace membership-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}")
            .WithTags("Workspace Memberships")
            .RequireAuthorization();

        // List members
        group.MapGet("/members", ListMembers)
            .WithName("ListWorkspaceMembers")
            .WithOpenApi();

        // Create invite
        group.MapPost("/invites", CreateInvite)
            .WithName("CreateWorkspaceInvite")
            .WithOpenApi();

        // List invites
        group.MapGet("/invites", ListInvites)
            .WithName("ListWorkspaceInvites")
            .WithOpenApi();

        // Accept invite (uses token, not workspaceId route)
        app.MapPost("/api/v1/workspaces/invites/{token}/accept", AcceptInvite)
            .WithTags("Workspace Memberships")
            .WithName("AcceptWorkspaceInvite")
            .WithOpenApi()
            .RequireAuthorization();

        // Update member role
        group.MapPut("/members/{membershipId:guid}", UpdateMemberRole)
            .WithName("UpdateWorkspaceMemberRole")
            .WithOpenApi();

        // Remove member
        group.MapDelete("/members/{membershipId:guid}", RemoveMember)
            .WithName("RemoveWorkspaceMember")
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Lists all members of a workspace.
    /// </summary>
    private static async Task<IResult> ListMembers(
        Guid workspaceId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Check access (any membership role is sufficient to view members)
        var role = await authZ.GetWorkspaceRoleAsync(workspaceId, cancellationToken);
        if (!role.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found or access denied." });
        }

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.WorkspaceMemberships
            .Where(m => m.WorkspaceId == workspaceId)
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

        var result = new PagedList<WorkspaceMembershipDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Creates an invitation to join the workspace.
    /// </summary>
    private static async Task<IResult> CreateInvite(
        Guid workspaceId,
        CreateWorkspaceInviteRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to invite users
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        // Validate email
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { error = "Validation Error", message = "Email is required." });
        }

        var email = request.Email.Trim().ToLowerInvariant();

        // Validate role - cannot invite as Owner
        var domainRole = MapToDomainRole(request.Role);
        if (domainRole == WorkspaceRole.Owner)
        {
            return Results.BadRequest(new { error = "Validation Error", message = "Cannot invite users as Owner. Use role change after membership is created." });
        }

        // Check if user is already a member
        var existingUser = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email, cancellationToken);

        if (existingUser != null)
        {
            var existingMembership = await dbContext.WorkspaceMemberships
                .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == existingUser.Id, cancellationToken);

            if (existingMembership)
            {
                return Results.Conflict(new { error = "Conflict", message = "User is already a member of this workspace." });
            }
        }

        // Check for existing pending invite
        var existingInvite = await dbContext.WorkspaceInvites
            .FirstOrDefaultAsync(i => i.WorkspaceId == workspaceId
                && i.Email.ToLower() == email
                && i.AcceptedAt == null
                && i.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

        if (existingInvite != null)
        {
            return Results.Conflict(new { error = "Conflict", message = "A pending invite already exists for this email." });
        }

        // Generate unique token
        var token = GenerateInviteToken();

        var invite = new WorkspaceInvite
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Email = email,
            Role = domainRole,
            Token = token,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) // 7 day expiry
        };

        dbContext.WorkspaceInvites.Add(invite);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/workspaces/{workspaceId}/invites", MapInviteToDto(invite));
    }

    /// <summary>
    /// Lists all invites for a workspace.
    /// </summary>
    private static async Task<IResult> ListInvites(
        Guid workspaceId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        bool includePending = true,
        bool includeAccepted = false,
        bool includeExpired = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to view invites
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var now = DateTimeOffset.UtcNow;
        var query = dbContext.WorkspaceInvites
            .Where(i => i.WorkspaceId == workspaceId);

        // Apply filters
        if (!includePending && !includeAccepted && !includeExpired)
        {
            // Default: show pending only
            includePending = true;
        }

        query = query.Where(i =>
            (includePending && i.AcceptedAt == null && i.ExpiresAt > now) ||
            (includeAccepted && i.AcceptedAt != null) ||
            (includeExpired && i.AcceptedAt == null && i.ExpiresAt <= now));

        query = query.OrderByDescending(i => i.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(i => MapInviteToDto(i))
            .ToListAsync(cancellationToken);

        var result = new PagedList<WorkspaceInviteDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Accepts a workspace invitation using the token.
    /// </summary>
    private static async Task<IResult> AcceptInvite(
        string token,
        IUserContext userContext,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Find the invite by token
        var invite = await dbContext.WorkspaceInvites
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);

        if (invite == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Invitation not found." });
        }

        // Check if already accepted
        if (invite.AcceptedAt.HasValue)
        {
            return Results.BadRequest(new { error = "Invalid Operation", message = "This invitation has already been accepted." });
        }

        // Check if expired
        if (invite.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Results.BadRequest(new { error = "Invalid Operation", message = "This invitation has expired." });
        }

        // Check if user is already a member
        var existingMembership = await dbContext.WorkspaceMemberships
            .AnyAsync(m => m.WorkspaceId == invite.WorkspaceId && m.UserId == userContext.UserId.Value, cancellationToken);

        if (existingMembership)
        {
            return Results.Conflict(new { error = "Conflict", message = "You are already a member of this workspace." });
        }

        // Create membership
        var membership = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = invite.WorkspaceId,
            UserId = userContext.UserId.Value,
            Role = invite.Role,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.WorkspaceMemberships.Add(membership);

        // Mark invite as accepted
        invite.AcceptedAt = DateTimeOffset.UtcNow;
        invite.AcceptedByUserId = userContext.UserId.Value;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Get the user info for the response
        var user = await dbContext.Users.FindAsync(new object[] { userContext.UserId.Value }, cancellationToken);
        membership.User = user;

        return Results.Ok(MapToDto(membership));
    }

    /// <summary>
    /// Updates a workspace member's role.
    /// </summary>
    private static async Task<IResult> UpdateMemberRole(
        Guid workspaceId,
        Guid membershipId,
        UpdateWorkspaceMemberRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Get current user's role
        var currentUserRole = await authZ.GetWorkspaceRoleAsync(workspaceId, cancellationToken);
        if (!currentUserRole.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found or access denied." });
        }

        // Find the membership to update
        var membership = await dbContext.WorkspaceMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.WorkspaceId == workspaceId, cancellationToken);

        if (membership == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Membership not found." });
        }

        var newRole = MapToDomainRole(request.Role);

        // Authorization rules for role changes:
        // 1. Only Owner can promote/demote to/from Owner
        // 2. Only Owner/Admin can promote/demote to/from Admin
        // 3. Owner/Admin can change Member/Guest roles

        // Current user must be at least Admin to change any roles
        if (currentUserRole.Value < WorkspaceRole.Admin)
        {
            return Results.Forbid();
        }

        // Owner role changes require Owner permission
        if (membership.Role == WorkspaceRole.Owner || newRole == WorkspaceRole.Owner)
        {
            if (currentUserRole.Value != WorkspaceRole.Owner)
            {
                return Results.Forbid();
            }
        }

        // Cannot change your own role (prevents Owner from demoting themselves)
        if (membership.UserId == userContext.UserId.Value)
        {
            return Results.BadRequest(new { error = "Invalid Operation", message = "You cannot change your own role." });
        }

        // Prevent removing the last Owner
        if (membership.Role == WorkspaceRole.Owner && newRole != WorkspaceRole.Owner)
        {
            var ownerCount = await dbContext.WorkspaceMemberships
                .CountAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner, cancellationToken);

            if (ownerCount <= 1)
            {
                return Results.BadRequest(new { error = "Invalid Operation", message = "Cannot demote the last Owner. Transfer ownership first." });
            }
        }

        membership.Role = newRole;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(MapToDto(membership));
    }

    /// <summary>
    /// Removes a member from the workspace.
    /// </summary>
    private static async Task<IResult> RemoveMember(
        Guid workspaceId,
        Guid membershipId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Get current user's role
        var currentUserRole = await authZ.GetWorkspaceRoleAsync(workspaceId, cancellationToken);
        if (!currentUserRole.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Workspace not found or access denied." });
        }

        // Find the membership to remove
        var membership = await dbContext.WorkspaceMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.WorkspaceId == workspaceId, cancellationToken);

        if (membership == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Membership not found." });
        }

        // Authorization rules for removal:
        // 1. Users can remove themselves (leave workspace) unless they're the last Owner
        // 2. Owner can remove anyone except themselves if they're the last Owner
        // 3. Admin can remove Member/Guest only

        var isSelf = membership.UserId == userContext.UserId.Value;

        if (isSelf)
        {
            // User is leaving the workspace
            if (membership.Role == WorkspaceRole.Owner)
            {
                var ownerCount = await dbContext.WorkspaceMemberships
                    .CountAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner, cancellationToken);

                if (ownerCount <= 1)
                {
                    return Results.BadRequest(new { error = "Invalid Operation", message = "Cannot leave as the last Owner. Transfer ownership first." });
                }
            }
        }
        else
        {
            // User is removing someone else
            if (currentUserRole.Value < WorkspaceRole.Admin)
            {
                return Results.Forbid();
            }

            // Only Owner can remove Owner/Admin
            if (membership.Role >= WorkspaceRole.Admin && currentUserRole.Value != WorkspaceRole.Owner)
            {
                return Results.Forbid();
            }
        }

        dbContext.WorkspaceMemberships.Remove(membership);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static string GenerateInviteToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static WorkspaceMembershipDto MapToDto(WorkspaceMembership membership)
    {
        return new WorkspaceMembershipDto
        {
            Id = membership.Id,
            WorkspaceId = membership.WorkspaceId,
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

    private static WorkspaceInviteDto MapInviteToDto(WorkspaceInvite invite)
    {
        return new WorkspaceInviteDto
        {
            Id = invite.Id,
            WorkspaceId = invite.WorkspaceId,
            Email = invite.Email,
            Role = MapToContractRole(invite.Role),
            Token = invite.Token,
            CreatedAt = invite.CreatedAt,
            ExpiresAt = invite.ExpiresAt,
            IsAccepted = invite.AcceptedAt.HasValue,
            AcceptedAt = invite.AcceptedAt
        };
    }

    private static ContractWorkspaceRole MapToContractRole(WorkspaceRole role)
    {
        return role switch
        {
            WorkspaceRole.Guest => ContractWorkspaceRole.Guest,
            WorkspaceRole.Member => ContractWorkspaceRole.Member,
            WorkspaceRole.Admin => ContractWorkspaceRole.Admin,
            WorkspaceRole.Owner => ContractWorkspaceRole.Owner,
            _ => ContractWorkspaceRole.Guest
        };
    }

    private static WorkspaceRole MapToDomainRole(ContractWorkspaceRole role)
    {
        return role switch
        {
            ContractWorkspaceRole.Guest => WorkspaceRole.Guest,
            ContractWorkspaceRole.Member => WorkspaceRole.Member,
            ContractWorkspaceRole.Admin => WorkspaceRole.Admin,
            ContractWorkspaceRole.Owner => WorkspaceRole.Owner,
            _ => WorkspaceRole.Guest
        };
    }
}

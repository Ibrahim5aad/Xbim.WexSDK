using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Auth;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using DomainAuditEventType = Xbim.WexServer.Domain.Enums.PersonalAccessTokenAuditEventType;
using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using static Xbim.WexServer.Abstractions.Auth.OAuthScopes;

namespace Xbim.WexServer.Endpoints;

public static class PersonalAccessTokenEndpoints
{
    public static IEndpointRouteBuilder MapPersonalAccessTokenEndpoints(this IEndpointRouteBuilder app)
    {
        // User's own PATs (scoped to workspace)
        var userGroup = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/pats")
            .WithTags("Personal Access Tokens")
            .RequireAuthorization();

        userGroup.MapPost("", CreatePersonalAccessToken)
            .WithName("CreatePersonalAccessToken")
            .Produces<PersonalAccessTokenCreatedDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        userGroup.MapGet("", ListMyPersonalAccessTokens)
            .WithName("ListMyPersonalAccessTokens")
            .Produces<PagedList<PersonalAccessTokenDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        userGroup.MapGet("/{tokenId:guid}", GetPersonalAccessToken)
            .WithName("GetPersonalAccessToken")
            .Produces<PersonalAccessTokenDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        userGroup.MapPut("/{tokenId:guid}", UpdatePersonalAccessToken)
            .WithName("UpdatePersonalAccessToken")
            .Produces<PersonalAccessTokenDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        userGroup.MapDelete("/{tokenId:guid}", RevokePersonalAccessToken)
            .WithName("RevokePersonalAccessToken")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        userGroup.MapGet("/{tokenId:guid}/audit-logs", GetPersonalAccessTokenAuditLogs)
            .WithName("GetPersonalAccessTokenAuditLogs")
            .Produces<PagedList<PersonalAccessTokenAuditLogDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        // Admin endpoints for viewing all PATs in workspace
        var adminGroup = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/admin/pats")
            .WithTags("Personal Access Tokens (Admin)")
            .RequireAuthorization();

        adminGroup.MapGet("", ListWorkspacePersonalAccessTokens)
            .WithName("ListWorkspacePersonalAccessTokens")
            .Produces<PagedList<WorkspacePersonalAccessTokenSummaryDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        adminGroup.MapDelete("/{tokenId:guid}", AdminRevokePersonalAccessToken)
            .WithName("AdminRevokePersonalAccessToken")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> CreatePersonalAccessToken(
        Guid workspaceId,
        CreatePersonalAccessTokenRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        IOAuthTokenService tokenService,
        XbimDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require pats:write scope
        authZ.RequireScope(PatsWrite);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // User must be at least a member of the workspace to create PATs
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Member, cancellationToken);

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new ErrorResponse
            {
                Code = "ValidationError",
                Message = "Name is required."
            });
        }

        if (request.Name.Length > 200)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Code = "ValidationError",
                Message = "Name cannot exceed 200 characters."
            });
        }

        if (request.Scopes == null || request.Scopes.Count == 0)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Code = "ValidationError",
                Message = "At least one scope is required."
            });
        }

        // Validate expiration
        var expiresInDays = request.ExpiresInDays;
        if (expiresInDays < 1)
        {
            expiresInDays = tokenService.DefaultPersonalAccessTokenLifetimeDays;
        }
        if (expiresInDays > tokenService.MaxPersonalAccessTokenLifetimeDays)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Code = "ValidationError",
                Message = $"Token expiration cannot exceed {tokenService.MaxPersonalAccessTokenLifetimeDays} days."
            });
        }

        // Generate token
        var plainTextToken = tokenService.GeneratePersonalAccessToken();
        var tokenHash = tokenService.HashPersonalAccessToken(plainTextToken);
        var tokenPrefix = tokenService.GetPersonalAccessTokenPrefix(plainTextToken);

        var pat = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            TokenHash = tokenHash,
            TokenPrefix = tokenPrefix,
            UserId = userContext.UserId.Value,
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Scopes = string.Join(" ", request.Scopes),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(expiresInDays),
            IsRevoked = false,
            CreatedFromIpAddress = GetClientIpAddress(httpContext)
        };

        dbContext.PersonalAccessTokens.Add(pat);

        // Create audit log
        var auditLog = new PersonalAccessTokenAuditLog
        {
            Id = Guid.NewGuid(),
            PersonalAccessTokenId = pat.Id,
            EventType = DomainAuditEventType.Created,
            ActorUserId = userContext.UserId.Value,
            Timestamp = DateTimeOffset.UtcNow,
            Details = JsonSerializer.Serialize(new
            {
                Name = pat.Name,
                Scopes = request.Scopes,
                ExpiresAt = pat.ExpiresAt,
                ExpiresInDays = expiresInDays
            }),
            IpAddress = GetClientIpAddress(httpContext)
        };

        dbContext.PersonalAccessTokenAuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = new PersonalAccessTokenCreatedDto
        {
            Id = pat.Id,
            UserId = pat.UserId,
            WorkspaceId = pat.WorkspaceId,
            Name = pat.Name,
            Description = pat.Description,
            Token = plainTextToken,
            TokenPrefix = tokenPrefix,
            Scopes = request.Scopes,
            CreatedAt = pat.CreatedAt,
            ExpiresAt = pat.ExpiresAt
        };

        return Results.Created($"/api/v1/workspaces/{workspaceId}/pats/{pat.Id}", dto);
    }

    private static async Task<IResult> ListMyPersonalAccessTokens(
        Guid workspaceId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        int page = 1,
        int pageSize = 20,
        bool includeRevoked = false,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require pats:read scope
        authZ.RequireScope(PatsRead);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // User must be at least a member of the workspace
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Member, cancellationToken);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.PersonalAccessTokens
            .Where(t => t.UserId == userContext.UserId.Value && t.WorkspaceId == workspaceId);

        if (!includeRevoked)
        {
            query = query.Where(t => !t.IsRevoked);
        }

        query = query.OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(t => MapToDto(t))
            .ToListAsync(cancellationToken);

        var result = new PagedList<PersonalAccessTokenDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    private static async Task<IResult> GetPersonalAccessToken(
        Guid workspaceId,
        Guid tokenId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require pats:read scope
        authZ.RequireScope(PatsRead);

        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Member, cancellationToken);

        var pat = await dbContext.PersonalAccessTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.WorkspaceId == workspaceId && t.UserId == userContext.UserId.Value, cancellationToken);

        if (pat == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "Personal access token not found."
            });
        }

        return Results.Ok(MapToDto(pat));
    }

    private static async Task<IResult> UpdatePersonalAccessToken(
        Guid workspaceId,
        Guid tokenId,
        UpdatePersonalAccessTokenRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require pats:write scope
        authZ.RequireScope(PatsWrite);

        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Member, cancellationToken);

        var pat = await dbContext.PersonalAccessTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.WorkspaceId == workspaceId && t.UserId == userContext.UserId.Value, cancellationToken);

        if (pat == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "Personal access token not found."
            });
        }

        if (pat.IsRevoked)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Code = "InvalidOperation",
                Message = "Cannot update a revoked token."
            });
        }

        var changes = new Dictionary<string, object?>();
        var updated = false;

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ErrorResponse
                {
                    Code = "ValidationError",
                    Message = "Name cannot be empty."
                });
            }
            if (request.Name.Length > 200)
            {
                return Results.BadRequest(new ErrorResponse
                {
                    Code = "ValidationError",
                    Message = "Name cannot exceed 200 characters."
                });
            }
            changes["Name"] = new { Old = pat.Name, New = request.Name.Trim() };
            pat.Name = request.Name.Trim();
            updated = true;
        }

        if (request.Description != null)
        {
            changes["Description"] = new { Old = pat.Description, New = request.Description };
            pat.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            updated = true;
        }

        if (updated)
        {
            // Create audit log
            var auditLog = new PersonalAccessTokenAuditLog
            {
                Id = Guid.NewGuid(),
                PersonalAccessTokenId = pat.Id,
                EventType = DomainAuditEventType.Updated,
                ActorUserId = userContext.UserId.Value,
                Timestamp = DateTimeOffset.UtcNow,
                Details = JsonSerializer.Serialize(changes),
                IpAddress = GetClientIpAddress(httpContext)
            };
            dbContext.PersonalAccessTokenAuditLogs.Add(auditLog);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(MapToDto(pat));
    }

    private static async Task<IResult> RevokePersonalAccessToken(
        Guid workspaceId,
        Guid tokenId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require pats:write scope
        authZ.RequireScope(PatsWrite);

        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Member, cancellationToken);

        var pat = await dbContext.PersonalAccessTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.WorkspaceId == workspaceId && t.UserId == userContext.UserId.Value, cancellationToken);

        if (pat == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "Personal access token not found."
            });
        }

        if (pat.IsRevoked)
        {
            // Already revoked, return success
            return Results.NoContent();
        }

        pat.IsRevoked = true;
        pat.RevokedAt = DateTimeOffset.UtcNow;
        pat.RevokedReason = "user_revoked";

        // Create audit log
        var auditLog = new PersonalAccessTokenAuditLog
        {
            Id = Guid.NewGuid(),
            PersonalAccessTokenId = pat.Id,
            EventType = DomainAuditEventType.RevokedByUser,
            ActorUserId = userContext.UserId.Value,
            Timestamp = DateTimeOffset.UtcNow,
            Details = JsonSerializer.Serialize(new { Reason = "User revoked token" }),
            IpAddress = GetClientIpAddress(httpContext)
        };
        dbContext.PersonalAccessTokenAuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> GetPersonalAccessTokenAuditLogs(
        Guid workspaceId,
        Guid tokenId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require pats:read scope
        authZ.RequireScope(PatsRead);

        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Member, cancellationToken);

        // Verify PAT exists and belongs to user
        var patExists = await dbContext.PersonalAccessTokens
            .AnyAsync(t => t.Id == tokenId && t.WorkspaceId == workspaceId && t.UserId == userContext.UserId.Value, cancellationToken);

        if (!patExists)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "Personal access token not found."
            });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.PersonalAccessTokenAuditLogs
            .Where(l => l.PersonalAccessTokenId == tokenId)
            .OrderByDescending(l => l.Timestamp);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(l => l.ActorUser)
            .AsNoTracking()
            .Select(l => new PersonalAccessTokenAuditLogDto
            {
                Id = l.Id,
                PersonalAccessTokenId = l.PersonalAccessTokenId,
                EventType = (PersonalAccessTokenAuditEventType)l.EventType,
                ActorUserId = l.ActorUserId,
                ActorUserName = l.ActorUser != null ? l.ActorUser.DisplayName : null,
                Timestamp = l.Timestamp,
                Details = l.Details,
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent
            })
            .ToListAsync(cancellationToken);

        var result = new PagedList<PersonalAccessTokenAuditLogDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    private static async Task<IResult> ListWorkspacePersonalAccessTokens(
        Guid workspaceId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        int page = 1,
        int pageSize = 20,
        bool includeRevoked = false,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require pats:admin scope
        authZ.RequireScope(PatsAdmin);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to view all PATs in workspace
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.PersonalAccessTokens
            .Where(t => t.WorkspaceId == workspaceId);

        if (!includeRevoked)
        {
            query = query.Where(t => !t.IsRevoked);
        }

        query = query.OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(t => t.User)
            .AsNoTracking()
            .Select(t => new WorkspacePersonalAccessTokenSummaryDto
            {
                TokenId = t.Id,
                UserId = t.UserId,
                UserDisplayName = t.User != null ? t.User.DisplayName ?? "" : "",
                UserEmail = t.User != null ? t.User.Email ?? "" : "",
                TokenName = t.Name,
                TokenPrefix = t.TokenPrefix,
                Scopes = t.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                CreatedAt = t.CreatedAt,
                ExpiresAt = t.ExpiresAt,
                LastUsedAt = t.LastUsedAt,
                IsActive = !t.IsRevoked && t.ExpiresAt > DateTimeOffset.UtcNow
            })
            .ToListAsync(cancellationToken);

        var result = new PagedList<WorkspacePersonalAccessTokenSummaryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    private static async Task<IResult> AdminRevokePersonalAccessToken(
        Guid workspaceId,
        Guid tokenId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require pats:admin scope
        authZ.RequireScope(PatsAdmin);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to revoke other users' PATs
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        var pat = await dbContext.PersonalAccessTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.WorkspaceId == workspaceId, cancellationToken);

        if (pat == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "Personal access token not found."
            });
        }

        if (pat.IsRevoked)
        {
            // Already revoked, return success
            return Results.NoContent();
        }

        pat.IsRevoked = true;
        pat.RevokedAt = DateTimeOffset.UtcNow;
        pat.RevokedReason = "admin_revoked";

        // Create audit log
        var auditLog = new PersonalAccessTokenAuditLog
        {
            Id = Guid.NewGuid(),
            PersonalAccessTokenId = pat.Id,
            EventType = DomainAuditEventType.RevokedByAdmin,
            ActorUserId = userContext.UserId.Value,
            Timestamp = DateTimeOffset.UtcNow,
            Details = JsonSerializer.Serialize(new
            {
                Reason = "Admin revoked token",
                TokenOwner = pat.User?.Email ?? pat.UserId.ToString()
            }),
            IpAddress = GetClientIpAddress(httpContext)
        };
        dbContext.PersonalAccessTokenAuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static PersonalAccessTokenDto MapToDto(PersonalAccessToken pat)
    {
        var scopes = pat.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        return new PersonalAccessTokenDto
        {
            Id = pat.Id,
            UserId = pat.UserId,
            WorkspaceId = pat.WorkspaceId,
            Name = pat.Name,
            Description = pat.Description,
            TokenPrefix = pat.TokenPrefix,
            Scopes = scopes,
            CreatedAt = pat.CreatedAt,
            ExpiresAt = pat.ExpiresAt,
            LastUsedAt = pat.LastUsedAt,
            LastUsedIpAddress = pat.LastUsedIpAddress,
            IsRevoked = pat.IsRevoked,
            RevokedAt = pat.RevokedAt,
            IsActive = !pat.IsRevoked && pat.ExpiresAt > DateTimeOffset.UtcNow
        };
    }

    private static string? GetClientIpAddress(HttpContext httpContext)
    {
        // Try X-Forwarded-For first (for reverse proxies)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP if there are multiple
            return forwardedFor.Split(',')[0].Trim();
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }
}

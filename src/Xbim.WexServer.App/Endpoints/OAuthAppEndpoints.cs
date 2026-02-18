using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using DomainClientType = Xbim.WexServer.Domain.Enums.OAuthClientType;
using DomainAuditEventType = Xbim.WexServer.Domain.Enums.OAuthAppAuditEventType;
using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using static Xbim.WexServer.Abstractions.Auth.OAuthScopes;

namespace Xbim.WexServer.App.Endpoints;

public static class OAuthAppEndpoints
{
    public static IEndpointRouteBuilder MapOAuthAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/apps")
            .WithTags("OAuth Apps")
            .RequireAuthorization();

        group.MapPost("", CreateOAuthApp)
            .WithName("CreateOAuthApp")
            .Produces<OAuthAppCreatedDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        group.MapGet("", ListOAuthApps)
            .WithName("ListOAuthApps")
            .Produces<PagedList<OAuthAppDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        group.MapGet("/{appId:guid}", GetOAuthApp)
            .WithName("GetOAuthApp")
            .Produces<OAuthAppDto>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        group.MapPut("/{appId:guid}", UpdateOAuthApp)
            .WithName("UpdateOAuthApp")
            .Produces<OAuthAppDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        group.MapDelete("/{appId:guid}", DeleteOAuthApp)
            .WithName("DeleteOAuthApp")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        group.MapPost("/{appId:guid}/rotate-secret", RotateClientSecret)
            .WithName("RotateOAuthAppSecret")
            .Produces<OAuthAppSecretRotatedDto>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        group.MapGet("/{appId:guid}/audit-logs", GetAuditLogs)
            .WithName("GetOAuthAppAuditLogs")
            .Produces<PagedList<OAuthAppAuditLogDto>>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> CreateOAuthApp(
        Guid workspaceId,
        CreateOAuthAppRequest request,
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

        // Require oauth_apps:admin scope
        authZ.RequireScope(OAuthAppsAdmin);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to create apps
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

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

        // Validate redirect URIs
        foreach (var uri in request.RedirectUris)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) ||
                (parsedUri.Scheme != "https" && parsedUri.Scheme != "http" && !uri.StartsWith("com.") && !uri.Contains("://")))
            {
                // Allow https, http (for localhost dev), and custom schemes for mobile apps
                if (!Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Code = "ValidationError",
                        Message = $"Invalid redirect URI: {uri}"
                    });
                }
            }
        }

        var domainClientType = (DomainClientType)request.ClientType;

        // Generate client ID
        var clientId = GenerateClientId();

        // Generate client secret for confidential clients
        string? plainTextSecret = null;
        string? secretHash = null;
        if (domainClientType == DomainClientType.Confidential)
        {
            plainTextSecret = GenerateClientSecret();
            secretHash = HashSecret(plainTextSecret);
        }

        var app = new OAuthApp
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ClientType = domainClientType,
            ClientId = clientId,
            ClientSecretHash = secretHash,
            RedirectUris = JsonSerializer.Serialize(request.RedirectUris),
            AllowedScopes = string.Join(" ", request.AllowedScopes),
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userContext.UserId.Value
        };

        dbContext.OAuthApps.Add(app);

        // Create audit log
        var auditLog = new OAuthAppAuditLog
        {
            Id = Guid.NewGuid(),
            OAuthAppId = app.Id,
            EventType = DomainAuditEventType.Created,
            ActorUserId = userContext.UserId.Value,
            Timestamp = DateTimeOffset.UtcNow,
            Details = JsonSerializer.Serialize(new
            {
                Name = app.Name,
                ClientType = request.ClientType.ToString(),
                RedirectUris = request.RedirectUris,
                AllowedScopes = request.AllowedScopes
            }),
            IpAddress = GetClientIpAddress(httpContext)
        };

        dbContext.OAuthAppAuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = new OAuthAppCreatedDto
        {
            Id = app.Id,
            WorkspaceId = app.WorkspaceId,
            Name = app.Name,
            Description = app.Description,
            ClientType = request.ClientType,
            ClientId = app.ClientId,
            ClientSecret = plainTextSecret,
            RedirectUris = request.RedirectUris,
            AllowedScopes = request.AllowedScopes,
            IsEnabled = app.IsEnabled,
            CreatedAt = app.CreatedAt
        };

        return Results.Created($"/api/v1/workspaces/{workspaceId}/apps/{app.Id}", dto);
    }

    private static async Task<IResult> ListOAuthApps(
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

        // Require oauth_apps:read scope
        authZ.RequireScope(OAuthAppsRead);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to list apps
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.OAuthApps
            .Where(a => a.WorkspaceId == workspaceId)
            .OrderByDescending(a => a.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(a => MapToDto(a))
            .ToListAsync(cancellationToken);

        var result = new PagedList<OAuthAppDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    private static async Task<IResult> GetOAuthApp(
        Guid workspaceId,
        Guid appId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require oauth_apps:read scope
        authZ.RequireScope(OAuthAppsRead);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to view app details
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        var app = await dbContext.OAuthApps
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appId && a.WorkspaceId == workspaceId, cancellationToken);

        if (app == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "OAuth app not found."
            });
        }

        return Results.Ok(MapToDto(app));
    }

    private static async Task<IResult> UpdateOAuthApp(
        Guid workspaceId,
        Guid appId,
        UpdateOAuthAppRequest request,
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

        // Require oauth_apps:write scope
        authZ.RequireScope(OAuthAppsWrite);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to update apps
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        var app = await dbContext.OAuthApps
            .FirstOrDefaultAsync(a => a.Id == appId && a.WorkspaceId == workspaceId, cancellationToken);

        if (app == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "OAuth app not found."
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
            changes["Name"] = new { Old = app.Name, New = request.Name.Trim() };
            app.Name = request.Name.Trim();
            updated = true;
        }

        if (request.Description != null)
        {
            changes["Description"] = new { Old = app.Description, New = request.Description };
            app.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            updated = true;
        }

        if (request.RedirectUris != null)
        {
            var oldUris = JsonSerializer.Deserialize<List<string>>(app.RedirectUris) ?? new List<string>();
            changes["RedirectUris"] = new { Old = oldUris, New = request.RedirectUris };
            app.RedirectUris = JsonSerializer.Serialize(request.RedirectUris);
            updated = true;
        }

        if (request.AllowedScopes != null)
        {
            var oldScopes = app.AllowedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            changes["AllowedScopes"] = new { Old = oldScopes, New = request.AllowedScopes };
            app.AllowedScopes = string.Join(" ", request.AllowedScopes);
            updated = true;
        }

        if (request.IsEnabled.HasValue && request.IsEnabled.Value != app.IsEnabled)
        {
            changes["IsEnabled"] = new { Old = app.IsEnabled, New = request.IsEnabled.Value };
            app.IsEnabled = request.IsEnabled.Value;
            updated = true;

            // Create specific audit log for enable/disable
            var enableDisableLog = new OAuthAppAuditLog
            {
                Id = Guid.NewGuid(),
                OAuthAppId = app.Id,
                EventType = request.IsEnabled.Value ? DomainAuditEventType.Enabled : DomainAuditEventType.Disabled,
                ActorUserId = userContext.UserId.Value,
                Timestamp = DateTimeOffset.UtcNow,
                IpAddress = GetClientIpAddress(httpContext)
            };
            dbContext.OAuthAppAuditLogs.Add(enableDisableLog);
        }

        if (updated)
        {
            app.UpdatedAt = DateTimeOffset.UtcNow;

            // Create update audit log (if there are non-enable/disable changes)
            if (changes.Any(c => c.Key != "IsEnabled"))
            {
                var auditLog = new OAuthAppAuditLog
                {
                    Id = Guid.NewGuid(),
                    OAuthAppId = app.Id,
                    EventType = DomainAuditEventType.Updated,
                    ActorUserId = userContext.UserId.Value,
                    Timestamp = DateTimeOffset.UtcNow,
                    Details = JsonSerializer.Serialize(changes.Where(c => c.Key != "IsEnabled").ToDictionary(c => c.Key, c => c.Value)),
                    IpAddress = GetClientIpAddress(httpContext)
                };
                dbContext.OAuthAppAuditLogs.Add(auditLog);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(MapToDto(app));
    }

    private static async Task<IResult> DeleteOAuthApp(
        Guid workspaceId,
        Guid appId,
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

        // Require oauth_apps:admin scope
        authZ.RequireScope(OAuthAppsAdmin);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to delete apps
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        var app = await dbContext.OAuthApps
            .FirstOrDefaultAsync(a => a.Id == appId && a.WorkspaceId == workspaceId, cancellationToken);

        if (app == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "OAuth app not found."
            });
        }

        // Create audit log before deletion (will be cascade deleted with the app)
        // So we need to log to a different mechanism if we want to preserve after deletion
        // For now, the audit log will be deleted with the app
        var auditLog = new OAuthAppAuditLog
        {
            Id = Guid.NewGuid(),
            OAuthAppId = app.Id,
            EventType = DomainAuditEventType.Deleted,
            ActorUserId = userContext.UserId.Value,
            Timestamp = DateTimeOffset.UtcNow,
            Details = JsonSerializer.Serialize(new
            {
                Name = app.Name,
                ClientId = app.ClientId
            }),
            IpAddress = GetClientIpAddress(httpContext)
        };
        dbContext.OAuthAppAuditLogs.Add(auditLog);

        // Note: In production, you might want to soft-delete or archive audit logs
        dbContext.OAuthApps.Remove(app);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> RotateClientSecret(
        Guid workspaceId,
        Guid appId,
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

        // Require oauth_apps:admin scope
        authZ.RequireScope(OAuthAppsAdmin);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to rotate secrets
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        var app = await dbContext.OAuthApps
            .FirstOrDefaultAsync(a => a.Id == appId && a.WorkspaceId == workspaceId, cancellationToken);

        if (app == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "OAuth app not found."
            });
        }

        if (app.ClientType == DomainClientType.Public)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Code = "InvalidOperation",
                Message = "Public clients do not have client secrets."
            });
        }

        // Generate new secret
        var plainTextSecret = GenerateClientSecret();
        var secretHash = HashSecret(plainTextSecret);

        app.ClientSecretHash = secretHash;
        app.UpdatedAt = DateTimeOffset.UtcNow;

        // Create audit log
        var auditLog = new OAuthAppAuditLog
        {
            Id = Guid.NewGuid(),
            OAuthAppId = app.Id,
            EventType = DomainAuditEventType.SecretRotated,
            ActorUserId = userContext.UserId.Value,
            Timestamp = DateTimeOffset.UtcNow,
            IpAddress = GetClientIpAddress(httpContext)
        };
        dbContext.OAuthAppAuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new OAuthAppSecretRotatedDto
        {
            AppId = app.Id,
            ClientId = app.ClientId,
            ClientSecret = plainTextSecret,
            RotatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> GetAuditLogs(
        Guid workspaceId,
        Guid appId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require oauth_apps:read scope
        authZ.RequireScope(OAuthAppsRead);

        // Enforce workspace isolation - token can only access its bound workspace
        authZ.RequireWorkspaceIsolation(workspaceId);

        // Require Admin role to view audit logs
        await authZ.RequireWorkspaceAccessAsync(workspaceId, WorkspaceRole.Admin, cancellationToken);

        // Verify app exists and belongs to workspace
        var appExists = await dbContext.OAuthApps
            .AnyAsync(a => a.Id == appId && a.WorkspaceId == workspaceId, cancellationToken);

        if (!appExists)
        {
            return Results.NotFound(new ErrorResponse
            {
                Code = "NotFound",
                Message = "OAuth app not found."
            });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.OAuthAppAuditLogs
            .Where(l => l.OAuthAppId == appId)
            .OrderByDescending(l => l.Timestamp);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(l => l.ActorUser)
            .AsNoTracking()
            .Select(l => new OAuthAppAuditLogDto
            {
                Id = l.Id,
                OAuthAppId = l.OAuthAppId,
                EventType = (OAuthAppAuditEventType)l.EventType,
                ActorUserId = l.ActorUserId,
                ActorUserName = l.ActorUser != null ? l.ActorUser.DisplayName : null,
                Timestamp = l.Timestamp,
                Details = l.Details,
                IpAddress = l.IpAddress
            })
            .ToListAsync(cancellationToken);

        var result = new PagedList<OAuthAppAuditLogDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    private static OAuthAppDto MapToDto(OAuthApp app)
    {
        var redirectUris = JsonSerializer.Deserialize<List<string>>(app.RedirectUris) ?? new List<string>();
        var allowedScopes = app.AllowedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        return new OAuthAppDto
        {
            Id = app.Id,
            WorkspaceId = app.WorkspaceId,
            Name = app.Name,
            Description = app.Description,
            ClientType = (OAuthClientType)app.ClientType,
            ClientId = app.ClientId,
            RedirectUris = redirectUris,
            AllowedScopes = allowedScopes,
            IsEnabled = app.IsEnabled,
            CreatedAt = app.CreatedAt,
            UpdatedAt = app.UpdatedAt,
            CreatedByUserId = app.CreatedByUserId
        };
    }

    private static string GenerateClientId()
    {
        // Generate a URL-safe client ID: oct_<random>
        var bytes = new byte[24];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return "oct_" + Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string GenerateClientSecret()
    {
        // Generate a strong secret: 256-bit random value
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string HashSecret(string secret)
    {
        // Use PBKDF2 with SHA256 for hashing the client secret
        using var deriveBytes = new Rfc2898DeriveBytes(
            secret,
            saltSize: 16,
            iterations: 100000,
            HashAlgorithmName.SHA256);

        var salt = deriveBytes.Salt;
        var hash = deriveBytes.GetBytes(32);

        // Combine salt and hash for storage
        var combined = new byte[salt.Length + hash.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(hash, 0, combined, salt.Length, hash.Length);

        return Convert.ToBase64String(combined);
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

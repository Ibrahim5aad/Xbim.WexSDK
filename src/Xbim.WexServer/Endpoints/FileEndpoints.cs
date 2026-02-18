using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Persistence.EfCore;

using ProjectRole = Xbim.WexServer.Domain.Enums.ProjectRole;
using FileKind = Xbim.WexServer.Contracts.FileKind;
using FileCategory = Xbim.WexServer.Contracts.FileCategory;
using static Xbim.WexServer.Abstractions.Auth.OAuthScopes;

namespace Xbim.WexServer.Endpoints;

/// <summary>
/// File API endpoints for managing files.
/// </summary>
public static class FileEndpoints
{
    /// <summary>
    /// Maps file-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // Project-scoped file endpoints
        var projectFilesGroup = app.MapGroup("/api/v1/projects/{projectId:guid}/files")
            .WithTags("Files")
            .RequireAuthorization();

        projectFilesGroup.MapGet("", ListFiles)
            .WithName("ListFiles")
            .Produces<PagedList<FileDto>>()
            .WithOpenApi();

        // File-scoped endpoints (access by file ID)
        var filesGroup = app.MapGroup("/api/v1/files")
            .WithTags("Files")
            .RequireAuthorization();

        filesGroup.MapGet("/{fileId:guid}", GetFile)
            .WithName("GetFile")
            .Produces<FileDto>()
            .WithOpenApi();

        filesGroup.MapGet("/{fileId:guid}/content", GetFileContent)
            .WithName("GetFileContent")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .WithOpenApi();

        filesGroup.MapDelete("/{fileId:guid}", DeleteFile)
            .WithName("DeleteFile")
            .Produces<FileDto>()
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Gets a file by its ID.
    /// Requires at least Viewer role in the project that contains the file.
    /// Requires scope: files:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> GetFile(
        Guid fileId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require files:read scope
        authZ.RequireScope(FilesRead);

        // Find the file
        var file = await dbContext.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (file == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "File not found." });
        }

        // Enforce workspace isolation - token can only access files in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(file.ProjectId, cancellationToken);

        // Check project access - returns 404 if no access to avoid exposing file existence
        if (!await authZ.CanAccessProjectAsync(file.ProjectId, ProjectRole.Viewer, cancellationToken))
        {
            return Results.NotFound(new { error = "Not Found", message = "File not found." });
        }

        // Map to DTO
        var fileDto = new FileDto
        {
            Id = file.Id,
            ProjectId = file.ProjectId,
            Name = file.Name,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            Checksum = file.Checksum,
            Kind = (FileKind)(int)file.Kind,
            Category = (FileCategory)(int)file.Category,
            StorageProvider = file.StorageProvider,
            StorageKey = file.StorageKey,
            IsDeleted = file.IsDeleted,
            CreatedAt = file.CreatedAt,
            DeletedAt = file.DeletedAt
        };

        return Results.Ok(fileDto);
    }

    /// <summary>
    /// Downloads the content of a file by its ID.
    /// Requires at least Viewer role in the project that contains the file.
    /// Streams the file content directly from storage.
    /// Requires scope: files:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> GetFileContent(
        Guid fileId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        IStorageProvider storageProvider,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require files:read scope
        authZ.RequireScope(FilesRead);

        // Find the file
        var file = await dbContext.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (file == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "File not found." });
        }

        // Enforce workspace isolation - token can only access files in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(file.ProjectId, cancellationToken);

        // Check project access - returns 404 if no access to avoid exposing file existence
        if (!await authZ.CanAccessProjectAsync(file.ProjectId, ProjectRole.Viewer, cancellationToken))
        {
            return Results.NotFound(new { error = "Not Found", message = "File not found." });
        }

        // Check if file is deleted
        if (file.IsDeleted)
        {
            return Results.NotFound(new { error = "Not Found", message = "File has been deleted." });
        }

        // Check if storage key exists
        if (string.IsNullOrEmpty(file.StorageKey))
        {
            return Results.NotFound(new { error = "Not Found", message = "File content not available." });
        }

        // Open the file stream from storage
        var stream = await storageProvider.OpenReadAsync(file.StorageKey, cancellationToken);

        if (stream == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "File content not found in storage." });
        }

        // Determine content type (default to application/octet-stream if not specified)
        var contentType = !string.IsNullOrEmpty(file.ContentType)
            ? file.ContentType
            : "application/octet-stream";

        // Return the file stream with appropriate headers
        return Results.File(
            stream,
            contentType: contentType,
            fileDownloadName: file.Name,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Soft deletes a file by its ID.
    /// Requires at least Editor role in the project that contains the file.
    /// Sets IsDeleted to true and records the deletion timestamp.
    /// Requires scope: files:write
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> DeleteFile(
        Guid fileId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require files:write scope
        authZ.RequireScope(FilesWrite);

        // Find the file
        var file = await dbContext.Files
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (file == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "File not found." });
        }

        // Enforce workspace isolation - token can only access files in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(file.ProjectId, cancellationToken);

        // Check project access - returns 404 if no access to avoid exposing file existence
        if (!await authZ.CanAccessProjectAsync(file.ProjectId, ProjectRole.Editor, cancellationToken))
        {
            return Results.NotFound(new { error = "Not Found", message = "File not found." });
        }

        // Check if file is already deleted
        if (file.IsDeleted)
        {
            return Results.BadRequest(new { error = "Bad Request", message = "File is already deleted." });
        }

        // Soft delete the file
        file.IsDeleted = true;
        file.DeletedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Map to DTO
        var fileDto = new FileDto
        {
            Id = file.Id,
            ProjectId = file.ProjectId,
            Name = file.Name,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            Checksum = file.Checksum,
            Kind = (FileKind)(int)file.Kind,
            Category = (FileCategory)(int)file.Category,
            StorageProvider = file.StorageProvider,
            StorageKey = file.StorageKey,
            IsDeleted = file.IsDeleted,
            CreatedAt = file.CreatedAt,
            DeletedAt = file.DeletedAt
        };

        return Results.Ok(fileDto);
    }

    /// <summary>
    /// Lists files in a project with optional filtering by kind and category.
    /// Requires at least Viewer role in the project.
    /// Requires scope: files:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> ListFiles(
        Guid projectId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        FileKind? kind = null,
        FileCategory? category = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require files:read scope
        authZ.RequireScope(FilesRead);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require at least Viewer role to list files
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.Viewer, cancellationToken);

        // Verify project exists
        var projectExists = await dbContext.Projects
            .AnyAsync(p => p.Id == projectId, cancellationToken);

        if (!projectExists)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found." });
        }

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Build query - exclude deleted files by default
        var query = dbContext.Files
            .Where(f => f.ProjectId == projectId && !f.IsDeleted);

        // Apply optional filters
        if (kind.HasValue)
        {
            var domainKind = (Domain.Enums.FileKind)(int)kind.Value;
            query = query.Where(f => f.Kind == domainKind);
        }

        if (category.HasValue)
        {
            var domainCategory = (Domain.Enums.FileCategory)(int)category.Value;
            query = query.Where(f => f.Category == domainCategory);
        }

        // Order by creation date descending (newest first)
        query = query.OrderByDescending(f => f.CreatedAt);

        // Get total count for pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Get paginated items
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(f => new FileDto
            {
                Id = f.Id,
                ProjectId = f.ProjectId,
                Name = f.Name,
                ContentType = f.ContentType,
                SizeBytes = f.SizeBytes,
                Checksum = f.Checksum,
                Kind = (FileKind)(int)f.Kind,
                Category = (FileCategory)(int)f.Category,
                StorageProvider = f.StorageProvider,
                StorageKey = f.StorageKey,
                IsDeleted = f.IsDeleted,
                CreatedAt = f.CreatedAt,
                DeletedAt = f.DeletedAt
            })
            .ToListAsync(cancellationToken);

        var result = new PagedList<FileDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }
}

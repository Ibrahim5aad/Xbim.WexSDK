using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.App.RateLimiting;
using Xbim.WexServer.App.Storage;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using ProjectRole = Xbim.WexServer.Domain.Enums.ProjectRole;
using DomainUploadSessionStatus = Xbim.WexServer.Domain.Enums.UploadSessionStatus;
using DomainUploadMode = Xbim.WexServer.Domain.Enums.UploadMode;
using FileKind = Xbim.WexServer.Contracts.FileKind;
using FileCategory = Xbim.WexServer.Contracts.FileCategory;
using UploadMode = Xbim.WexServer.Contracts.UploadMode;
using static Xbim.WexServer.Abstractions.Auth.OAuthScopes;

namespace Xbim.WexServer.App.Endpoints;

/// <summary>
/// File upload session endpoints for managing file uploads.
/// </summary>
public static class FileUploadEndpoints
{
    /// <summary>
    /// Default upload session expiration time.
    /// </summary>
    private static readonly TimeSpan DefaultSessionExpiration = TimeSpan.FromHours(24);

    /// <summary>
    /// Default maximum file size (500 MB).
    /// </summary>
    private const long DefaultMaxFileSizeBytes = 500L * 1024 * 1024;

    /// <summary>
    /// Maps file upload-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapFileUploadEndpoints(this IEndpointRouteBuilder app)
    {
        // Project-scoped upload session endpoints
        var uploadGroup = app.MapGroup("/api/v1/projects/{projectId:guid}/files/uploads")
            .WithTags("File Uploads")
            .RequireAuthorization();

        uploadGroup.MapPost("", ReserveUpload)
            .WithName("ReserveUpload")
            .Produces<ReserveUploadResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimitPolicies.UploadReserve)
            .WithOpenApi();

        uploadGroup.MapGet("/{sessionId:guid}", GetUploadSession)
            .WithName("GetUploadSession")
            .Produces<UploadSessionDto>()
            .WithOpenApi();

        uploadGroup.MapPost("/{sessionId:guid}/content", UploadContent)
            .WithName("UploadContent")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UploadContentResponse>()
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimitPolicies.UploadContent)
            .WithOpenApi()
            .DisableAntiforgery();

        uploadGroup.MapPost("/{sessionId:guid}/commit", CommitUpload)
            .WithName("CommitUpload")
            .Produces<CommitUploadResponse>()
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimitPolicies.UploadCommit)
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Reserves an upload session for a file. Returns session ID and upload constraints.
    /// If PreferDirectUpload is true and the storage provider supports it, returns a SAS URL for direct upload.
    /// Requires at least Editor role in the project.
    /// Requires scope: files:write
    /// </summary>
    private static async Task<IResult> ReserveUpload(
        Guid projectId,
        ReserveUploadRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        IStorageProvider storageProvider,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require files:write scope
        authZ.RequireScope(FilesWrite);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require at least Editor role to upload files
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.Editor, cancellationToken);

        // Get the project to determine the workspace
        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found." });
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new { error = "Validation Error", message = "FileName is required." });
        }

        // Get max file size from configuration or use default
        var maxFileSize = configuration.GetValue<long?>("Upload:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;

        // Validate expected size if provided
        if (request.ExpectedSizeBytes.HasValue)
        {
            if (request.ExpectedSizeBytes.Value <= 0)
            {
                return Results.BadRequest(new { error = "Validation Error", message = "ExpectedSizeBytes must be positive." });
            }

            if (request.ExpectedSizeBytes.Value > maxFileSize)
            {
                return Results.BadRequest(new { error = "Validation Error", message = $"File size exceeds maximum allowed size of {maxFileSize} bytes." });
            }
        }

        // Create upload session
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(DefaultSessionExpiration);

        // Get file extension for the storage key
        var fileExtension = Path.GetExtension(request.FileName);

        // Generate temp storage key
        var tempStorageKey = StorageKeyHelper.GenerateUploadKey(project.WorkspaceId, projectId, sessionId, fileExtension);

        // Determine upload mode and generate SAS URL if requested and supported
        var uploadMode = DomainUploadMode.ServerProxy;
        string? directUploadUrl = null;

        if (request.PreferDirectUpload && storageProvider.SupportsDirectUpload)
        {
            directUploadUrl = await storageProvider.GenerateUploadSasUrlAsync(
                tempStorageKey,
                request.ContentType?.Trim(),
                expiresAt,
                cancellationToken);

            if (!string.IsNullOrEmpty(directUploadUrl))
            {
                uploadMode = DomainUploadMode.DirectToBlob;
            }
        }

        var session = new UploadSession
        {
            Id = sessionId,
            ProjectId = projectId,
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType?.Trim(),
            ExpectedSizeBytes = request.ExpectedSizeBytes,
            Status = DomainUploadSessionStatus.Reserved,
            UploadMode = uploadMode,
            TempStorageKey = tempStorageKey,
            DirectUploadUrl = directUploadUrl,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        dbContext.UploadSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Return session with constraints
        var response = new ReserveUploadResponse
        {
            Session = MapToDto(session),
            Constraints = new UploadConstraints
            {
                MaxFileSizeBytes = maxFileSize,
                SessionExpiresAt = expiresAt
            }
        };

        return Results.Created($"/api/v1/projects/{projectId}/files/uploads/{session.Id}", response);
    }

    /// <summary>
    /// Gets an upload session by ID.
    /// Requires scope: files:read
    /// </summary>
    private static async Task<IResult> GetUploadSession(
        Guid projectId,
        Guid sessionId,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require files:read scope
        authZ.RequireScope(FilesRead);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require at least Viewer role to see upload sessions
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.Viewer, cancellationToken);

        var session = await dbContext.UploadSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.ProjectId == projectId, cancellationToken);

        if (session == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Upload session not found." });
        }

        return Results.Ok(MapToDto(session));
    }

    /// <summary>
    /// Uploads content to an existing upload session via multipart form data.
    /// Requires at least Editor role in the project.
    /// Requires scope: files:write
    /// </summary>
    private static async Task<IResult> UploadContent(
        Guid projectId,
        Guid sessionId,
        IFormFile file,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        IStorageProvider storageProvider,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require files:write scope
        authZ.RequireScope(FilesWrite);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require at least Editor role to upload files
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.Editor, cancellationToken);

        // Get the session
        var session = await dbContext.UploadSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.ProjectId == projectId, cancellationToken);

        if (session == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Upload session not found." });
        }

        // Validate session status - must be Reserved or Uploading
        if (session.Status != DomainUploadSessionStatus.Reserved &&
            session.Status != DomainUploadSessionStatus.Uploading)
        {
            return Results.BadRequest(new
            {
                error = "Invalid Session State",
                message = $"Cannot upload to session with status '{session.Status}'."
            });
        }

        // Validate session hasn't expired
        if (session.ExpiresAt < DateTimeOffset.UtcNow)
        {
            session.Status = DomainUploadSessionStatus.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.BadRequest(new
            {
                error = "Session Expired",
                message = "The upload session has expired."
            });
        }

        // Validate file is provided
        if (file.Length == 0)
        {
            return Results.BadRequest(new
            {
                error = "No File",
                message = "No file was provided in the request."
            });
        }

        // Get max file size from configuration or use default
        var maxFileSize = configuration.GetValue<long?>("Upload:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;

        // Validate file size
        if (file.Length > maxFileSize)
        {
            return Results.BadRequest(new
            {
                error = "File Too Large",
                message = $"File size ({file.Length} bytes) exceeds maximum allowed size ({maxFileSize} bytes)."
            });
        }

        // Validate against expected size if specified
        if (session.ExpectedSizeBytes.HasValue && file.Length != session.ExpectedSizeBytes.Value)
        {
            return Results.BadRequest(new
            {
                error = "Size Mismatch",
                message = $"File size ({file.Length} bytes) does not match expected size ({session.ExpectedSizeBytes.Value} bytes)."
            });
        }

        // Upload the file to storage
        if (string.IsNullOrEmpty(session.TempStorageKey))
        {
            return Results.Problem("Upload session is missing storage key.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            await storageProvider.PutAsync(
                session.TempStorageKey,
                stream,
                session.ContentType ?? file.ContentType,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Log and return error
            return Results.Problem($"Failed to store file: {ex.Message}");
        }

        // Update session status to Uploading
        session.Status = DomainUploadSessionStatus.Uploading;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Return response
        var response = new UploadContentResponse
        {
            Session = MapToDto(session),
            BytesUploaded = file.Length
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Commits an upload session, creating a File record and marking the session as committed.
    /// Requires at least Editor role in the project.
    /// Requires scope: files:write
    /// </summary>
    private static async Task<IResult> CommitUpload(
        Guid projectId,
        Guid sessionId,
        CommitUploadRequest? request,
        IUserContext userContext,
        IAuthorizationService authZ,
        XbimDbContext dbContext,
        IStorageProvider storageProvider,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require files:write scope
        authZ.RequireScope(FilesWrite);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require at least Editor role to commit uploads
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.Editor, cancellationToken);

        // Get the session
        var session = await dbContext.UploadSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.ProjectId == projectId, cancellationToken);

        if (session == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Upload session not found." });
        }

        // Validate session status
        // For server-proxy mode: must be Uploading (content has been uploaded via server)
        // For direct-to-blob mode: can be Reserved (content was uploaded directly to storage)
        var isDirectUpload = session.UploadMode == DomainUploadMode.DirectToBlob;

        if (session.Status == DomainUploadSessionStatus.Committed)
        {
            return Results.BadRequest(new
            {
                error = "Invalid Session State",
                message = "Upload session has already been committed."
            });
        }

        if (session.Status == DomainUploadSessionStatus.Reserved && !isDirectUpload)
        {
            return Results.BadRequest(new
            {
                error = "Invalid Session State",
                message = "Cannot commit session - no content has been uploaded yet. Use the upload content endpoint or request direct upload mode."
            });
        }

        if (session.Status != DomainUploadSessionStatus.Reserved &&
            session.Status != DomainUploadSessionStatus.Uploading)
        {
            return Results.BadRequest(new
            {
                error = "Invalid Session State",
                message = $"Cannot commit session with status '{session.Status}'."
            });
        }

        // Validate session hasn't expired
        if (session.ExpiresAt < DateTimeOffset.UtcNow)
        {
            session.Status = DomainUploadSessionStatus.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.BadRequest(new
            {
                error = "Session Expired",
                message = "The upload session has expired."
            });
        }

        // Verify file exists in storage
        if (string.IsNullOrEmpty(session.TempStorageKey))
        {
            return Results.Problem("Upload session is missing storage key.");
        }

        var exists = await storageProvider.ExistsAsync(session.TempStorageKey, cancellationToken);
        if (!exists)
        {
            session.Status = DomainUploadSessionStatus.Failed;
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.BadRequest(new
            {
                error = "Content Not Found",
                message = "Uploaded content not found in storage. Please re-upload."
            });
        }

        // Get file size from storage
        var sizeBytes = await storageProvider.GetSizeAsync(session.TempStorageKey, cancellationToken);
        if (!sizeBytes.HasValue)
        {
            return Results.Problem("Could not determine file size from storage.");
        }

        // Validate size against expected size if specified
        if (session.ExpectedSizeBytes.HasValue && sizeBytes.Value != session.ExpectedSizeBytes.Value)
        {
            return Results.BadRequest(new
            {
                error = "Size Mismatch",
                message = $"Stored file size ({sizeBytes.Value} bytes) does not match expected size ({session.ExpectedSizeBytes.Value} bytes)."
            });
        }

        // Determine file category from extension
        var fileCategory = DetermineFileCategory(session.FileName);

        // Create the File record
        var now = DateTimeOffset.UtcNow;
        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = session.FileName,
            ContentType = session.ContentType,
            SizeBytes = sizeBytes.Value,
            Checksum = request?.Checksum,
            Kind = Domain.Enums.FileKind.Source,
            Category = fileCategory,
            StorageProvider = storageProvider.ProviderId,
            StorageKey = session.TempStorageKey,
            IsDeleted = false,
            CreatedAt = now
        };

        dbContext.Files.Add(file);

        // Update session to committed
        session.Status = DomainUploadSessionStatus.Committed;
        session.CommittedFileId = file.Id;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Return response
        var response = new CommitUploadResponse
        {
            Session = MapToDto(session),
            File = MapFileToDto(file)
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Determines the file category based on file extension.
    /// </summary>
    private static Domain.Enums.FileCategory DetermineFileCategory(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".ifc" => Domain.Enums.FileCategory.Ifc,
            ".ifcxml" => Domain.Enums.FileCategory.Ifc,
            ".ifczip" => Domain.Enums.FileCategory.Ifc,
            ".wexbim" => Domain.Enums.FileCategory.WexBim,
            _ => Domain.Enums.FileCategory.Other
        };
    }

    private static UploadSessionDto MapToDto(UploadSession session)
    {
        return new UploadSessionDto
        {
            Id = session.Id,
            ProjectId = session.ProjectId,
            FileName = session.FileName,
            ContentType = session.ContentType,
            ExpectedSizeBytes = session.ExpectedSizeBytes,
            Status = (UploadSessionStatus)(int)session.Status,
            UploadMode = (UploadMode)(int)session.UploadMode,
            UploadUrl = session.DirectUploadUrl,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt
        };
    }

    private static FileDto MapFileToDto(FileEntity file)
    {
        return new FileDto
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
    }
}

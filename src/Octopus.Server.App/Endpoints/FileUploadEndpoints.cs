using Microsoft.EntityFrameworkCore;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.App.Storage;
using Octopus.Server.Contracts;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;

using ProjectRole = Octopus.Server.Domain.Enums.ProjectRole;
using DomainUploadSessionStatus = Octopus.Server.Domain.Enums.UploadSessionStatus;

namespace Octopus.Server.App.Endpoints;

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
            .WithOpenApi();

        uploadGroup.MapGet("/{sessionId:guid}", GetUploadSession)
            .WithName("GetUploadSession")
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Reserves an upload session for a file. Returns session ID and upload constraints.
    /// Requires at least Editor role in the project.
    /// </summary>
    private static async Task<IResult> ReserveUpload(
        Guid projectId,
        ReserveUploadRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

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

        var session = new UploadSession
        {
            Id = sessionId,
            ProjectId = projectId,
            FileName = request.FileName.Trim(),
            ContentType = request.ContentType?.Trim(),
            ExpectedSizeBytes = request.ExpectedSizeBytes,
            Status = DomainUploadSessionStatus.Reserved,
            TempStorageKey = tempStorageKey,
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

        return Results.Created($"/api/v1/projects/{projectId}/files/uploads/{sessionId}", response);
    }

    /// <summary>
    /// Gets an upload session by ID.
    /// </summary>
    private static async Task<IResult> GetUploadSession(
        Guid projectId,
        Guid sessionId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

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
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt
        };
    }
}

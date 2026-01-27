using Microsoft.EntityFrameworkCore;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Abstractions.Storage;
using Octopus.Server.App.Processing;
using Octopus.Server.Contracts;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;
using Octopus.Server.Processing;

using ProjectRole = Octopus.Server.Domain.Enums.ProjectRole;
using DomainProcessingStatus = Octopus.Server.Domain.Enums.ProcessingStatus;
using ContractProcessingStatus = Octopus.Server.Contracts.ProcessingStatus;

namespace Octopus.Server.App.Endpoints;

/// <summary>
/// ModelVersion API endpoints.
/// </summary>
public static class ModelVersionEndpoints
{
    /// <summary>
    /// Maps model version-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapModelVersionEndpoints(this IEndpointRouteBuilder app)
    {
        // Model-scoped endpoints for creating and listing versions
        var modelGroup = app.MapGroup("/api/v1/models/{modelId:guid}/versions")
            .WithTags("ModelVersions")
            .RequireAuthorization();

        modelGroup.MapPost("", CreateModelVersion)
            .WithName("CreateModelVersion")
            .Produces<ModelVersionDto>(StatusCodes.Status201Created)
            .WithOpenApi();

        modelGroup.MapGet("", ListModelVersions)
            .WithName("ListModelVersions")
            .Produces<PagedList<ModelVersionDto>>()
            .WithOpenApi();

        // Direct version endpoints for get
        var versionGroup = app.MapGroup("/api/v1/modelversions")
            .WithTags("ModelVersions")
            .RequireAuthorization();

        versionGroup.MapGet("/{versionId:guid}", GetModelVersion)
            .WithName("GetModelVersion")
            .Produces<ModelVersionDto>()
            .WithOpenApi();

        versionGroup.MapGet("/{versionId:guid}/wexbim", GetModelVersionWexBim)
            .WithName("GetModelVersionWexBim")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Creates a new version of a model from an existing IFC file.
    /// Requires Editor role or higher in the project.
    /// </summary>
    private static async Task<IResult> CreateModelVersion(
        Guid modelId,
        CreateModelVersionRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        IProcessingQueue processingQueue,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Find the model to get its project ID
        var model = await dbContext.Models
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken);

        if (model == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model not found." });
        }

        // Require at least Editor role to create versions
        await authZ.RequireProjectAccessAsync(model.ProjectId, ProjectRole.Editor, cancellationToken);

        // Validate the IFC file exists and is accessible
        var ifcFile = await dbContext.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.IfcFileId, cancellationToken);

        if (ifcFile == null)
        {
            return Results.BadRequest(new { error = "Validation Error", message = "IFC file not found." });
        }

        // Verify the file belongs to the same project as the model
        if (ifcFile.ProjectId != model.ProjectId)
        {
            return Results.BadRequest(new { error = "Validation Error", message = "IFC file must belong to the same project as the model." });
        }

        // Verify the file is not deleted
        if (ifcFile.IsDeleted)
        {
            return Results.BadRequest(new { error = "Validation Error", message = "Cannot create version from a deleted file." });
        }

        // Calculate the next version number
        var maxVersionNumber = await dbContext.ModelVersions
            .Where(v => v.ModelId == modelId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var version = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = modelId,
            VersionNumber = maxVersionNumber + 1,
            IfcFileId = request.IfcFileId,
            Status = DomainProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ModelVersions.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Enqueue the IFC to WexBIM conversion job
        await processingQueue.EnqueueAsync(
            IfcToWexBimJobHandler.JobTypeName,
            new IfcToWexBimJobPayload { ModelVersionId = version.Id },
            cancellationToken);

        var dto = MapToDto(version);
        return Results.Created($"/api/v1/modelversions/{version.Id}", dto);
    }

    /// <summary>
    /// Lists all versions of a model. Requires Viewer role or higher.
    /// </summary>
    private static async Task<IResult> ListModelVersions(
        Guid modelId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Find the model to get its project ID
        var model = await dbContext.Models
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken);

        if (model == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model not found." });
        }

        // Check project access (Viewer or higher)
        var role = await authZ.GetProjectRoleAsync(model.ProjectId, cancellationToken);
        if (!role.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model not found or access denied." });
        }

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.ModelVersions
            .Where(v => v.ModelId == modelId)
            .OrderByDescending(v => v.VersionNumber);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(v => MapToDto(v))
            .ToListAsync(cancellationToken);

        var result = new PagedList<ModelVersionDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Gets a model version by ID. Requires Viewer role or higher in the containing project.
    /// </summary>
    private static async Task<IResult> GetModelVersion(
        Guid versionId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Find the version with its model to get the project ID
        var version = await dbContext.ModelVersions
            .AsNoTracking()
            .Include(v => v.Model)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        // Check access to the containing project (Viewer or higher)
        var role = await authZ.GetProjectRoleAsync(version.Model!.ProjectId, cancellationToken);
        if (!role.HasValue)
        {
            // Return 404 to avoid revealing version existence
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        return Results.Ok(MapToDto(version));
    }

    /// <summary>
    /// Streams the WexBIM artifact for a model version.
    /// Requires Viewer role or higher in the containing project.
    /// Returns 404 if no WexBIM artifact exists.
    /// </summary>
    private static async Task<IResult> GetModelVersionWexBim(
        Guid versionId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        IStorageProvider storageProvider,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Find the version with its model and WexBIM file
        var version = await dbContext.ModelVersions
            .AsNoTracking()
            .Include(v => v.Model)
            .Include(v => v.WexBimFile)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        // Check access to the containing project (Viewer or higher)
        var role = await authZ.GetProjectRoleAsync(version.Model!.ProjectId, cancellationToken);
        if (!role.HasValue)
        {
            // Return 404 to avoid revealing version existence
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        // Check if WexBIM artifact exists
        if (!version.WexBimFileId.HasValue || version.WexBimFile == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "WexBIM artifact not found for this model version." });
        }

        var wexBimFile = version.WexBimFile;

        // Check if the file is deleted
        if (wexBimFile.IsDeleted)
        {
            return Results.NotFound(new { error = "Not Found", message = "WexBIM artifact has been deleted." });
        }

        // Check if storage key exists
        if (string.IsNullOrEmpty(wexBimFile.StorageKey))
        {
            return Results.NotFound(new { error = "Not Found", message = "WexBIM artifact content not available." });
        }

        // Open the file stream from storage
        var stream = await storageProvider.OpenReadAsync(wexBimFile.StorageKey, cancellationToken);

        if (stream == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "WexBIM artifact content not found in storage." });
        }

        // Determine content type (default to application/octet-stream if not specified)
        var contentType = !string.IsNullOrEmpty(wexBimFile.ContentType)
            ? wexBimFile.ContentType
            : "application/octet-stream";

        // Return the file stream with appropriate headers
        return Results.File(
            stream,
            contentType: contentType,
            fileDownloadName: wexBimFile.Name,
            enableRangeProcessing: true);
    }

    private static ModelVersionDto MapToDto(ModelVersion version)
    {
        return new ModelVersionDto
        {
            Id = version.Id,
            ModelId = version.ModelId,
            VersionNumber = version.VersionNumber,
            IfcFileId = version.IfcFileId,
            WexBimFileId = version.WexBimFileId,
            PropertiesFileId = version.PropertiesFileId,
            Status = (ContractProcessingStatus)version.Status,
            ErrorMessage = version.ErrorMessage,
            CreatedAt = version.CreatedAt,
            ProcessedAt = version.ProcessedAt
        };
    }
}

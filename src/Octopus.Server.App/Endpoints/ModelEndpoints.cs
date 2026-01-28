using Microsoft.EntityFrameworkCore;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.Contracts;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;

using ProjectRole = Octopus.Server.Domain.Enums.ProjectRole;
using static Octopus.Server.Abstractions.Auth.OAuthScopes;

namespace Octopus.Server.App.Endpoints;

/// <summary>
/// Model API endpoints.
/// </summary>
public static class ModelEndpoints
{
    /// <summary>
    /// Maps model-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder app)
    {
        // Project-scoped endpoints for creating and listing models
        var projectGroup = app.MapGroup("/api/v1/projects/{projectId:guid}/models")
            .WithTags("Models")
            .RequireAuthorization();

        projectGroup.MapPost("", CreateModel)
            .WithName("CreateModel")
            .Produces<ModelDto>(StatusCodes.Status201Created)
            .WithOpenApi();

        projectGroup.MapGet("", ListModels)
            .WithName("ListModels")
            .Produces<PagedList<ModelDto>>()
            .WithOpenApi();

        // Direct model endpoints for get/update
        var modelGroup = app.MapGroup("/api/v1/models")
            .WithTags("Models")
            .RequireAuthorization();

        modelGroup.MapGet("/{modelId:guid}", GetModel)
            .WithName("GetModel")
            .Produces<ModelDto>()
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Creates a new model in a project. Requires Editor role or higher in the project.
    /// Requires scope: models:write
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> CreateModel(
        Guid projectId,
        CreateModelRequest request,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Require models:write scope
        authZ.RequireScope(ModelsWrite);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Require at least Editor role to create models in a project
        await authZ.RequireProjectAccessAsync(projectId, ProjectRole.Editor, cancellationToken);

        // Validate the project exists
        var projectExists = await dbContext.Projects
            .AnyAsync(p => p.Id == projectId, cancellationToken);

        if (!projectExists)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Validation Error", message = "Name is required." });
        }

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Models.Add(model);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = MapToDto(model);
        return Results.Created($"/api/v1/models/{model.Id}", dto);
    }

    /// <summary>
    /// Lists all models in a project. Requires Viewer role or higher.
    /// Requires scope: models:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> ListModels(
        Guid projectId,
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

        // Require models:read scope
        authZ.RequireScope(ModelsRead);

        // Enforce workspace isolation - token can only access projects in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(projectId, cancellationToken);

        // Check project access (Viewer or higher)
        var role = await authZ.GetProjectRoleAsync(projectId, cancellationToken);
        if (!role.HasValue)
        {
            return Results.NotFound(new { error = "Not Found", message = "Project not found or access denied." });
        }

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Models
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(m => MapToDto(m))
            .ToListAsync(cancellationToken);

        var result = new PagedList<ModelDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Gets a model by ID. Requires Viewer role or higher in the containing project.
    /// Requires scope: models:read
    /// Enforces workspace isolation when token has tid claim.
    /// </summary>
    private static async Task<IResult> GetModel(
        Guid modelId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Require models:read scope
        authZ.RequireScope(ModelsRead);

        // First, find the model to get its project ID
        var model = await dbContext.Models
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken);

        if (model == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model not found." });
        }

        // Enforce workspace isolation - token can only access models in its bound workspace
        await authZ.RequireProjectWorkspaceIsolationAsync(model.ProjectId, cancellationToken);

        // Check access to the containing project (Viewer or higher)
        var role = await authZ.GetProjectRoleAsync(model.ProjectId, cancellationToken);
        if (!role.HasValue)
        {
            // Return 404 to avoid revealing model existence
            return Results.NotFound(new { error = "Not Found", message = "Model not found." });
        }

        return Results.Ok(MapToDto(model));
    }

    private static ModelDto MapToDto(Model model)
    {
        return new ModelDto
        {
            Id = model.Id,
            ProjectId = model.ProjectId,
            Name = model.Name,
            Description = model.Description,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }
}

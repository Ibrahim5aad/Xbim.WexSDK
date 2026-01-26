using Microsoft.EntityFrameworkCore;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.Contracts;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;
using ProjectRole = Octopus.Server.Domain.Enums.ProjectRole;

namespace Octopus.Server.App.Endpoints;

/// <summary>
/// Properties API endpoints for querying IFC element properties.
/// </summary>
public static class PropertiesEndpoints
{
    /// <summary>
    /// Maps properties-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPropertiesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/modelversions/{modelVersionId:guid}/properties")
            .WithTags("Properties")
            .RequireAuthorization();

        group.MapGet("", QueryProperties)
            .WithName("QueryProperties")
            .WithSummary("Query IFC element properties with filtering and paging")
            .WithDescription("Returns a paged list of IFC elements with their properties. Filter by entity label, global ID, type name, or property set name. Always returns paged results to prevent large response payloads.")
            .WithOpenApi();

        group.MapGet("/elements/{elementId:guid}", GetElementProperties)
            .WithName("GetElementProperties")
            .WithSummary("Get properties for a specific element")
            .WithDescription("Returns all properties and quantities for a single IFC element by its ID.")
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Query properties with filtering and paging.
    /// </summary>
    private static async Task<IResult> QueryProperties(
        Guid modelVersionId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        int? entityLabel = null,
        string? globalId = null,
        string? typeName = null,
        string? propertySetName = null,
        string? name = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Find the model version with its model to get the project ID
        var modelVersion = await dbContext.ModelVersions
            .AsNoTracking()
            .Include(v => v.Model)
            .FirstOrDefaultAsync(v => v.Id == modelVersionId, cancellationToken);

        if (modelVersion == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        // Check access to the containing project (Viewer or higher)
        var role = await authZ.GetProjectRoleAsync(modelVersion.Model!.ProjectId, cancellationToken);
        if (!role.HasValue)
        {
            // Return 404 to avoid revealing version existence
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        // Validate pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Build the query with filters
        var query = dbContext.IfcElements
            .Where(e => e.ModelVersionId == modelVersionId)
            .AsNoTracking();

        // Apply filters
        if (entityLabel.HasValue)
        {
            query = query.Where(e => e.EntityLabel == entityLabel.Value);
        }

        if (!string.IsNullOrWhiteSpace(globalId))
        {
            query = query.Where(e => e.GlobalId == globalId);
        }

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            query = query.Where(e => e.TypeName != null && e.TypeName.Contains(typeName));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(e => e.Name != null && e.Name.Contains(name));
        }

        // Filter by property set name - requires a join
        if (!string.IsNullOrWhiteSpace(propertySetName))
        {
            query = query.Where(e => e.PropertySets.Any(ps => ps.Name.Contains(propertySetName)));
        }

        // Order by entity label for consistent paging
        query = query.OrderBy(e => e.EntityLabel);

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Get paged items with properties and quantities
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(e => e.PropertySets)
                .ThenInclude(ps => ps.Properties)
            .Include(e => e.QuantitySets)
                .ThenInclude(qs => qs.Quantities)
            .ToListAsync(cancellationToken);

        var result = new PagedList<IfcElementDto>
        {
            Items = items.Select(MapToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(result);
    }

    /// <summary>
    /// Get properties for a specific element.
    /// </summary>
    private static async Task<IResult> GetElementProperties(
        Guid modelVersionId,
        Guid elementId,
        IUserContext userContext,
        IAuthorizationService authZ,
        OctopusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.IsAuthenticated || !userContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // Find the model version with its model to get the project ID
        var modelVersion = await dbContext.ModelVersions
            .AsNoTracking()
            .Include(v => v.Model)
            .FirstOrDefaultAsync(v => v.Id == modelVersionId, cancellationToken);

        if (modelVersion == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        // Check access to the containing project (Viewer or higher)
        var role = await authZ.GetProjectRoleAsync(modelVersion.Model!.ProjectId, cancellationToken);
        if (!role.HasValue)
        {
            // Return 404 to avoid revealing version existence
            return Results.NotFound(new { error = "Not Found", message = "Model version not found." });
        }

        // Get the element with properties and quantities
        var element = await dbContext.IfcElements
            .AsNoTracking()
            .Include(e => e.PropertySets)
                .ThenInclude(ps => ps.Properties)
            .Include(e => e.QuantitySets)
                .ThenInclude(qs => qs.Quantities)
            .FirstOrDefaultAsync(e => e.Id == elementId && e.ModelVersionId == modelVersionId, cancellationToken);

        if (element == null)
        {
            return Results.NotFound(new { error = "Not Found", message = "Element not found." });
        }

        return Results.Ok(MapToDto(element));
    }

    private static IfcElementDto MapToDto(IfcElement element)
    {
        return new IfcElementDto
        {
            Id = element.Id,
            ModelVersionId = element.ModelVersionId,
            EntityLabel = element.EntityLabel,
            GlobalId = element.GlobalId,
            Name = element.Name,
            TypeName = element.TypeName,
            Description = element.Description,
            ObjectType = element.ObjectType,
            TypeObjectName = element.TypeObjectName,
            TypeObjectType = element.TypeObjectType,
            ExtractedAt = element.ExtractedAt,
            PropertySets = element.PropertySets.Select(MapPropertySetToDto).ToList(),
            QuantitySets = element.QuantitySets.Select(MapQuantitySetToDto).ToList()
        };
    }

    private static IfcPropertySetDto MapPropertySetToDto(IfcPropertySet propertySet)
    {
        return new IfcPropertySetDto
        {
            Id = propertySet.Id,
            Name = propertySet.Name,
            GlobalId = propertySet.GlobalId,
            IsTypePropertySet = propertySet.IsTypePropertySet,
            Properties = propertySet.Properties.Select(MapPropertyToDto).ToList()
        };
    }

    private static IfcPropertyDto MapPropertyToDto(IfcProperty property)
    {
        return new IfcPropertyDto
        {
            Id = property.Id,
            Name = property.Name,
            Value = property.Value,
            ValueType = property.ValueType,
            Unit = property.Unit
        };
    }

    private static IfcQuantitySetDto MapQuantitySetToDto(IfcQuantitySet quantitySet)
    {
        return new IfcQuantitySetDto
        {
            Id = quantitySet.Id,
            Name = quantitySet.Name,
            GlobalId = quantitySet.GlobalId,
            Quantities = quantitySet.Quantities.Select(MapQuantityToDto).ToList()
        };
    }

    private static IfcQuantityDto MapQuantityToDto(IfcQuantity quantity)
    {
        return new IfcQuantityDto
        {
            Id = quantity.Id,
            Name = quantity.Name,
            Value = quantity.Value,
            ValueType = quantity.ValueType,
            Unit = quantity.Unit
        };
    }
}

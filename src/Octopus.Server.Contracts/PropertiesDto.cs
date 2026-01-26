namespace Octopus.Server.Contracts;

/// <summary>
/// Represents an IFC element with its properties and quantities.
/// </summary>
public record IfcElementDto
{
    public Guid Id { get; init; }
    public Guid ModelVersionId { get; init; }
    public int EntityLabel { get; init; }
    public string? GlobalId { get; init; }
    public string? Name { get; init; }
    public string? TypeName { get; init; }
    public string? Description { get; init; }
    public string? ObjectType { get; init; }
    public string? TypeObjectName { get; init; }
    public string? TypeObjectType { get; init; }
    public DateTimeOffset ExtractedAt { get; init; }
    public IReadOnlyList<IfcPropertySetDto> PropertySets { get; init; } = Array.Empty<IfcPropertySetDto>();
    public IReadOnlyList<IfcQuantitySetDto> QuantitySets { get; init; } = Array.Empty<IfcQuantitySetDto>();
}

/// <summary>
/// Represents an IFC property set.
/// </summary>
public record IfcPropertySetDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? GlobalId { get; init; }
    public bool IsTypePropertySet { get; init; }
    public IReadOnlyList<IfcPropertyDto> Properties { get; init; } = Array.Empty<IfcPropertyDto>();
}

/// <summary>
/// Represents an individual IFC property.
/// </summary>
public record IfcPropertyDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Value { get; init; }
    public string ValueType { get; init; } = "string";
    public string? Unit { get; init; }
}

/// <summary>
/// Represents an IFC quantity set.
/// </summary>
public record IfcQuantitySetDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? GlobalId { get; init; }
    public IReadOnlyList<IfcQuantityDto> Quantities { get; init; } = Array.Empty<IfcQuantityDto>();
}

/// <summary>
/// Represents an individual IFC quantity.
/// </summary>
public record IfcQuantityDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public double? Value { get; init; }
    public string ValueType { get; init; } = "unknown";
    public string? Unit { get; init; }
}

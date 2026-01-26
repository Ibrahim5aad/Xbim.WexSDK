namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents an IFC element with its extracted properties.
/// </summary>
public class IfcElement
{
    public Guid Id { get; set; }
    public Guid ModelVersionId { get; set; }

    /// <summary>
    /// The IFC entity label (unique within the model).
    /// </summary>
    public int EntityLabel { get; set; }

    /// <summary>
    /// The IFC GlobalId (GUID).
    /// </summary>
    public string? GlobalId { get; set; }

    /// <summary>
    /// Element name from IFC.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// IFC type name (e.g., IfcWall, IfcDoor).
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Element description from IFC.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Object type from IFC.
    /// </summary>
    public string? ObjectType { get; set; }

    /// <summary>
    /// Type object name if element has a type.
    /// </summary>
    public string? TypeObjectName { get; set; }

    /// <summary>
    /// Type object IFC type (e.g., IfcWallType).
    /// </summary>
    public string? TypeObjectType { get; set; }

    public DateTimeOffset ExtractedAt { get; set; }

    // Navigation properties
    public ModelVersion? ModelVersion { get; set; }
    public ICollection<IfcPropertySet> PropertySets { get; set; } = new List<IfcPropertySet>();
    public ICollection<IfcQuantitySet> QuantitySets { get; set; } = new List<IfcQuantitySet>();
}

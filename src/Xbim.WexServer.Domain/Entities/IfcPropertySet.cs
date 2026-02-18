namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents an IFC property set belonging to an element.
/// </summary>
public class IfcPropertySet
{
    public Guid Id { get; set; }
    public Guid ElementId { get; set; }

    /// <summary>
    /// Property set name (e.g., Pset_WallCommon).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Property set GlobalId from IFC.
    /// </summary>
    public string? GlobalId { get; set; }

    /// <summary>
    /// Whether this is a type property set (from IfcTypeObject).
    /// </summary>
    public bool IsTypePropertySet { get; set; }

    // Navigation properties
    public IfcElement? Element { get; set; }
    public ICollection<IfcProperty> Properties { get; set; } = new List<IfcProperty>();
}

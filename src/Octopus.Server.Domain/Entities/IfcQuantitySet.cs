namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents an IFC quantity set belonging to an element.
/// </summary>
public class IfcQuantitySet
{
    public Guid Id { get; set; }
    public Guid ElementId { get; set; }

    /// <summary>
    /// Quantity set name (e.g., Qto_WallBaseQuantities).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Quantity set GlobalId from IFC.
    /// </summary>
    public string? GlobalId { get; set; }

    // Navigation properties
    public IfcElement? Element { get; set; }
    public ICollection<IfcQuantity> Quantities { get; set; } = new List<IfcQuantity>();
}

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents an individual IFC property within a property set.
/// </summary>
public class IfcProperty
{
    public Guid Id { get; set; }
    public Guid PropertySetId { get; set; }

    /// <summary>
    /// Property name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Property value as string.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Value type (string, boolean, integer, double, enumeration, etc.).
    /// </summary>
    public string ValueType { get; set; } = "string";

    /// <summary>
    /// Unit of measurement if applicable.
    /// </summary>
    public string? Unit { get; set; }

    // Navigation properties
    public IfcPropertySet? PropertySet { get; set; }
}

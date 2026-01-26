namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents an individual IFC quantity within a quantity set.
/// </summary>
public class IfcQuantity
{
    public Guid Id { get; set; }
    public Guid QuantitySetId { get; set; }

    /// <summary>
    /// Quantity name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Quantity value.
    /// </summary>
    public double? Value { get; set; }

    /// <summary>
    /// Value type (length, area, volume, count, weight, time).
    /// </summary>
    public string ValueType { get; set; } = "unknown";

    /// <summary>
    /// Unit of measurement.
    /// </summary>
    public string? Unit { get; set; }

    // Navigation properties
    public IfcQuantitySet? QuantitySet { get; set; }
}

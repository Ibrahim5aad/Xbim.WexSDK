namespace Xbim.WexServer.Processing;

/// <summary>
/// Payload for IFC properties extraction job.
/// </summary>
public record ExtractPropertiesJobPayload
{
    /// <summary>
    /// The model version to extract properties from.
    /// </summary>
    public required Guid ModelVersionId { get; init; }

    /// <summary>
    /// Whether to persist properties to the Xbim database for querying.
    /// When true, properties are stored in database tables (IfcElements, IfcPropertySets, etc.).
    /// Default is false (only creates SQLite artifact file).
    /// </summary>
    public bool PersistToDatabase { get; init; } = false;
}

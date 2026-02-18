namespace Xbim.WexServer.Processing;

/// <summary>
/// Payload for IFC to WexBIM conversion job.
/// </summary>
public record IfcToWexBimJobPayload
{
    /// <summary>
    /// The model version to process.
    /// </summary>
    public required Guid ModelVersionId { get; init; }
}

namespace Octopus.Blazor.Services.Abstractions;

/// <summary>
/// Service for managing and providing <see cref="IWexBimSource"/> instances.
/// <para>
/// This service acts as a registry for WexBIM sources, allowing components
/// to discover and use available sources without direct knowledge of their types.
/// </para>
/// </summary>
public interface IWexBimSourceProvider
{
    /// <summary>
    /// Gets all registered sources.
    /// </summary>
    IReadOnlyCollection<IWexBimSource> Sources { get; }

    /// <summary>
    /// Gets all available sources (sources where <see cref="IWexBimSource.IsAvailable"/> is true).
    /// </summary>
    IEnumerable<IWexBimSource> GetAvailableSources();

    /// <summary>
    /// Gets a source by its ID.
    /// </summary>
    /// <param name="sourceId">The source ID.</param>
    /// <returns>The source, or null if not found.</returns>
    IWexBimSource? GetSource(string sourceId);

    /// <summary>
    /// Registers a source with the provider.
    /// </summary>
    /// <param name="source">The source to register.</param>
    void RegisterSource(IWexBimSource source);

    /// <summary>
    /// Unregisters a source by its ID.
    /// </summary>
    /// <param name="sourceId">The ID of the source to unregister.</param>
    /// <returns>True if the source was removed; false if it was not found.</returns>
    bool UnregisterSource(string sourceId);

    /// <summary>
    /// Clears all registered sources.
    /// </summary>
    void ClearSources();

    /// <summary>
    /// Occurs when sources are added, removed, or the collection changes.
    /// </summary>
    event EventHandler? SourcesChanged;
}

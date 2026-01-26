using System.Collections.Concurrent;
using Octopus.Blazor.Services.Abstractions;

namespace Octopus.Blazor.Services;

/// <summary>
/// Default implementation of <see cref="IWexBimSourceProvider"/>.
/// <para>
/// Thread-safe singleton service for managing WexBIM sources.
/// </para>
/// </summary>
public class WexBimSourceProvider : IWexBimSourceProvider
{
    private readonly ConcurrentDictionary<string, IWexBimSource> _sources = new();

    /// <inheritdoc/>
    public IReadOnlyCollection<IWexBimSource> Sources => _sources.Values.ToList().AsReadOnly();

    /// <inheritdoc/>
    public event EventHandler? SourcesChanged;

    /// <inheritdoc/>
    public IEnumerable<IWexBimSource> GetAvailableSources()
    {
        return _sources.Values.Where(s => s.IsAvailable);
    }

    /// <inheritdoc/>
    public IWexBimSource? GetSource(string sourceId)
    {
        _sources.TryGetValue(sourceId, out var source);
        return source;
    }

    /// <inheritdoc/>
    public void RegisterSource(IWexBimSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _sources[source.Id] = source;
        OnSourcesChanged();
    }

    /// <inheritdoc/>
    public bool UnregisterSource(string sourceId)
    {
        if (_sources.TryRemove(sourceId, out _))
        {
            OnSourcesChanged();
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public void ClearSources()
    {
        _sources.Clear();
        OnSourcesChanged();
    }

    /// <summary>
    /// Raises the <see cref="SourcesChanged"/> event.
    /// </summary>
    protected virtual void OnSourcesChanged()
    {
        SourcesChanged?.Invoke(this, EventArgs.Empty);
    }
}

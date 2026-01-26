namespace Octopus.Blazor.Services.WexBimSources;

using Octopus.Blazor.Services.Abstractions;

/// <summary>
/// Base class for <see cref="IWexBimSource"/> implementations providing common functionality.
/// </summary>
public abstract class WexBimSourceBase : IWexBimSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WexBimSourceBase"/> class.
    /// </summary>
    /// <param name="id">Unique identifier for this source instance.</param>
    /// <param name="name">Display name of the source.</param>
    /// <param name="sourceType">The type of source.</param>
    protected WexBimSourceBase(string id, string name, WexBimSourceType sourceType)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        SourceType = sourceType;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public WexBimSourceType SourceType { get; }

    /// <inheritdoc/>
    public abstract bool IsAvailable { get; }

    /// <inheritdoc/>
    public abstract bool SupportsDirectUrl { get; }

    /// <inheritdoc/>
    public abstract Task<byte[]?> GetDataAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public abstract Task<string?> GetUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a unique identifier based on a prefix and a source-specific value.
    /// </summary>
    /// <param name="prefix">The prefix for the ID (e.g., "url", "file", "memory").</param>
    /// <param name="source">The source-specific identifier (e.g., URL, file path).</param>
    /// <returns>A unique identifier string.</returns>
    protected static string GenerateId(string prefix, string source)
    {
        var hash = Math.Abs(source.GetHashCode()).ToString("X8");
        return $"{prefix}-{hash}";
    }
}

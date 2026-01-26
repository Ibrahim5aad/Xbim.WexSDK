namespace Octopus.Blazor.Services.WexBimSources;

using Octopus.Blazor.Services.Abstractions;

/// <summary>
/// A WexBIM source that provides data from an in-memory byte array.
/// <para>
/// Use this source when the WexBIM data is already loaded into memory,
/// for example after processing an IFC file or receiving data from another source.
/// </para>
/// </summary>
/// <remarks>
/// Compatible with both Blazor WebAssembly and Blazor Server.
/// </remarks>
public class InMemoryWexBimSource : WexBimSourceBase
{
    private byte[]? _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryWexBimSource"/> class.
    /// </summary>
    /// <param name="data">The WexBIM data as a byte array.</param>
    /// <param name="name">Display name for this source.</param>
    /// <param name="id">Optional unique identifier. Generated if not provided.</param>
    public InMemoryWexBimSource(byte[] data, string name, string? id = null)
        : base(id ?? GenerateId("memory", Guid.NewGuid().ToString("N")), name, WexBimSourceType.InMemory)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc/>
    public override bool IsAvailable => _data != null;

    /// <inheritdoc/>
    /// <remarks>
    /// In-memory sources do not support direct URL loading.
    /// </remarks>
    public override bool SupportsDirectUrl => false;

    /// <summary>
    /// Gets the size of the WexBIM data in bytes.
    /// </summary>
    public long SizeBytes => _data?.Length ?? 0;

    /// <inheritdoc/>
    public override Task<byte[]?> GetDataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_data);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns null as in-memory sources do not support direct URL loading.
    /// </remarks>
    public override Task<string?> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Updates the in-memory data.
    /// </summary>
    /// <param name="data">The new WexBIM data.</param>
    public void UpdateData(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Clears the in-memory data to free memory.
    /// </summary>
    public void Clear()
    {
        _data = null;
    }
}

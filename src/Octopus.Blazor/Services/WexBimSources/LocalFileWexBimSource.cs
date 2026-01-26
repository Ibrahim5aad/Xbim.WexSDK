namespace Octopus.Blazor.Services.WexBimSources;

using Octopus.Blazor.Services.Abstractions;

/// <summary>
/// A WexBIM source that loads data from a local file path.
/// <para>
/// <strong>Note:</strong> This source is only compatible with Blazor Server or ASP.NET Core
/// environments where the server has file system access. It will not work in Blazor WebAssembly.
/// </para>
/// </summary>
public class LocalFileWexBimSource : WexBimSourceBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileWexBimSource"/> class.
    /// </summary>
    /// <param name="filePath">The absolute path to the WexBIM file.</param>
    /// <param name="name">Optional display name. Defaults to the filename.</param>
    public LocalFileWexBimSource(string filePath, string? name = null)
        : base(
            GenerateId("file", filePath ?? throw new ArgumentNullException(nameof(filePath))),
            name ?? Path.GetFileName(filePath),
            WexBimSourceType.LocalFile)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// Gets the file path of the WexBIM file.
    /// </summary>
    public string FilePath { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns true if the file exists at the specified path.
    /// </remarks>
    public override bool IsAvailable => File.Exists(FilePath);

    /// <inheritdoc/>
    /// <remarks>
    /// Local file sources do not support direct URL loading.
    /// </remarks>
    public override bool SupportsDirectUrl => false;

    /// <inheritdoc/>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    public override async Task<byte[]?> GetDataAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException($"WexBIM file not found at path: {FilePath}", FilePath);
        }

        try
        {
            return await File.ReadAllBytesAsync(FilePath, cancellationToken);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Failed to read WexBIM file at '{FilePath}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns null as local file sources do not support direct URL loading.
    /// </remarks>
    public override Task<string?> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Gets file information for the WexBIM file.
    /// </summary>
    /// <returns>FileInfo for the WexBIM file, or null if the file doesn't exist.</returns>
    public FileInfo? GetFileInfo()
    {
        return File.Exists(FilePath) ? new FileInfo(FilePath) : null;
    }
}

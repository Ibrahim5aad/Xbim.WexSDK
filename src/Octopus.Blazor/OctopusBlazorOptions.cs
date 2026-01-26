using Octopus.Blazor.Models;

namespace Octopus.Blazor;

/// <summary>
/// Configuration options for Octopus.Blazor services.
/// </summary>
public class OctopusBlazorOptions
{
    /// <summary>
    /// Gets or sets the initial viewer theme. Defaults to <see cref="ViewerTheme.Dark"/>.
    /// </summary>
    public ViewerTheme InitialTheme { get; set; } = ViewerTheme.Dark;

    /// <summary>
    /// Gets or sets the accent color for light theme. Defaults to "#0969da".
    /// </summary>
    public string LightAccentColor { get; set; } = "#0969da";

    /// <summary>
    /// Gets or sets the accent color for dark theme. Defaults to "#1e7e34".
    /// </summary>
    public string DarkAccentColor { get; set; } = "#1e7e34";

    /// <summary>
    /// Gets or sets the background color for light theme. Defaults to "#ffffff".
    /// </summary>
    public string LightBackgroundColor { get; set; } = "#ffffff";

    /// <summary>
    /// Gets or sets the background color for dark theme. Defaults to "#404040".
    /// </summary>
    public string DarkBackgroundColor { get; set; } = "#404040";

    /// <summary>
    /// Gets or sets the standalone WexBIM source configuration.
    /// <para>
    /// Configure this to automatically load WexBIM models from static assets, URLs, or local files.
    /// </para>
    /// </summary>
    public StandaloneSourceOptions? StandaloneSources { get; set; }
}

/// <summary>
/// Configuration options for standalone WexBIM sources.
/// </summary>
public class StandaloneSourceOptions
{
    /// <summary>
    /// Gets the list of static asset sources (relative paths within wwwroot).
    /// </summary>
    public List<StaticAssetSourceConfig> StaticAssets { get; } = new();

    /// <summary>
    /// Gets the list of URL sources (HTTP/HTTPS URLs).
    /// </summary>
    public List<UrlSourceConfig> Urls { get; } = new();

    /// <summary>
    /// Gets the list of local file sources (absolute file paths, Blazor Server only).
    /// </summary>
    public List<LocalFileSourceConfig> LocalFiles { get; } = new();

    /// <summary>
    /// Adds a static asset source from wwwroot.
    /// </summary>
    /// <param name="relativePath">Relative path within wwwroot (e.g., "models/sample.wexbim").</param>
    /// <param name="name">Optional display name.</param>
    /// <returns>This instance for chaining.</returns>
    public StandaloneSourceOptions AddStaticAsset(string relativePath, string? name = null)
    {
        StaticAssets.Add(new StaticAssetSourceConfig { RelativePath = relativePath, Name = name });
        return this;
    }

    /// <summary>
    /// Adds a URL source.
    /// </summary>
    /// <param name="url">The URL of the WexBIM file.</param>
    /// <param name="name">Optional display name.</param>
    /// <returns>This instance for chaining.</returns>
    public StandaloneSourceOptions AddUrl(string url, string? name = null)
    {
        Urls.Add(new UrlSourceConfig { Url = url, Name = name });
        return this;
    }

    /// <summary>
    /// Adds a local file source (Blazor Server only).
    /// </summary>
    /// <param name="filePath">The absolute path to the WexBIM file.</param>
    /// <param name="name">Optional display name.</param>
    /// <returns>This instance for chaining.</returns>
    public StandaloneSourceOptions AddLocalFile(string filePath, string? name = null)
    {
        LocalFiles.Add(new LocalFileSourceConfig { FilePath = filePath, Name = name });
        return this;
    }
}

/// <summary>
/// Configuration for a static asset WexBIM source.
/// </summary>
public class StaticAssetSourceConfig
{
    /// <summary>
    /// Gets or sets the relative path within wwwroot.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional display name.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// Configuration for a URL WexBIM source.
/// </summary>
public class UrlSourceConfig
{
    /// <summary>
    /// Gets or sets the URL of the WexBIM file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional display name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets the optional HTTP headers to include in requests.
    /// </summary>
    public Dictionary<string, string> Headers { get; } = new();
}

/// <summary>
/// Configuration for a local file WexBIM source.
/// </summary>
public class LocalFileSourceConfig
{
    /// <summary>
    /// Gets or sets the absolute path to the WexBIM file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional display name.
    /// </summary>
    public string? Name { get; set; }
}

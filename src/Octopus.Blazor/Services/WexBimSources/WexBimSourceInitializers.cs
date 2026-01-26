using Octopus.Blazor.Services.Abstractions;

namespace Octopus.Blazor.Services.WexBimSources;

/// <summary>
/// Interface for initializing WexBIM sources at application startup.
/// </summary>
public interface IWexBimSourceInitializer
{
    /// <summary>
    /// Initializes and registers sources with the provider.
    /// </summary>
    /// <param name="provider">The source provider to register sources with.</param>
    void Initialize(IWexBimSourceProvider provider);
}

/// <summary>
/// No-op initializer when no sources are configured.
/// </summary>
internal class NoOpSourceInitializer : IWexBimSourceInitializer
{
    public void Initialize(IWexBimSourceProvider provider) { }
}

/// <summary>
/// Initializes standalone sources (URL and local file) that don't require HttpClient.
/// </summary>
internal class StandaloneSourceInitializer : IWexBimSourceInitializer
{
    private readonly StandaloneSourceOptions _options;

    public StandaloneSourceInitializer(StandaloneSourceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Initialize(IWexBimSourceProvider provider)
    {
        // Register URL sources (without HttpClient - will need it for GetDataAsync)
        foreach (var urlConfig in _options.Urls)
        {
            var source = new UrlWexBimSource(
                urlConfig.Url,
                urlConfig.Name,
                httpClient: null,
                urlConfig.Headers.Count > 0 ? urlConfig.Headers : null);
            provider.RegisterSource(source);
        }

        // Register local file sources
        foreach (var fileConfig in _options.LocalFiles)
        {
            var source = new LocalFileWexBimSource(fileConfig.FilePath, fileConfig.Name);
            provider.RegisterSource(source);
        }
    }
}

/// <summary>
/// Initializes sources that require HttpClient (static assets and URL sources).
/// </summary>
internal class HttpClientSourceInitializer : IWexBimSourceInitializer
{
    private readonly StandaloneSourceOptions _options;
    private readonly HttpClient _httpClient;

    public HttpClientSourceInitializer(StandaloneSourceOptions options, HttpClient httpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public void Initialize(IWexBimSourceProvider provider)
    {
        // Register static asset sources
        foreach (var assetConfig in _options.StaticAssets)
        {
            var source = new StaticAssetWexBimSource(
                assetConfig.RelativePath,
                _httpClient,
                assetConfig.Name);
            provider.RegisterSource(source);
        }

        // Register URL sources with HttpClient
        foreach (var urlConfig in _options.Urls)
        {
            var source = new UrlWexBimSource(
                urlConfig.Url,
                urlConfig.Name,
                _httpClient,
                urlConfig.Headers.Count > 0 ? urlConfig.Headers : null);
            provider.RegisterSource(source);
        }

        // Register local file sources
        foreach (var fileConfig in _options.LocalFiles)
        {
            var source = new LocalFileWexBimSource(fileConfig.FilePath, fileConfig.Name);
            provider.RegisterSource(source);
        }
    }
}

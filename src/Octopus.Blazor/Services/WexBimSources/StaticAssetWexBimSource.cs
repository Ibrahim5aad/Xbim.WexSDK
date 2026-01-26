namespace Octopus.Blazor.Services.WexBimSources;

using Octopus.Blazor.Services.Abstractions;

/// <summary>
/// A WexBIM source that loads data from static web assets (wwwroot).
/// <para>
/// This source is optimized for loading WexBIM files from the application's
/// static asset directory (wwwroot) in both Blazor WebAssembly and Blazor Server.
/// </para>
/// </summary>
/// <remarks>
/// In Blazor WebAssembly, the HttpClient base address should be configured to the host.
/// In Blazor Server, use the relative path from wwwroot.
/// </remarks>
public class StaticAssetWexBimSource : WexBimSourceBase
{
    private readonly HttpClient _httpClient;
    private readonly string _baseAddress;

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticAssetWexBimSource"/> class.
    /// </summary>
    /// <param name="relativePath">The relative path to the WexBIM file within wwwroot (e.g., "models/sample.wexbim").</param>
    /// <param name="httpClient">The HttpClient configured with the base address pointing to the host.</param>
    /// <param name="name">Optional display name. Defaults to the filename.</param>
    public StaticAssetWexBimSource(string relativePath, HttpClient httpClient, string? name = null)
        : base(
            GenerateId("asset", relativePath ?? throw new ArgumentNullException(nameof(relativePath))),
            name ?? Path.GetFileName(relativePath),
            WexBimSourceType.Url)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        RelativePath = relativePath.TrimStart('/');
        _httpClient = httpClient;
        _baseAddress = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;
    }

    /// <summary>
    /// Gets the relative path to the WexBIM file within wwwroot.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets the full URL to the WexBIM file.
    /// </summary>
    public string FullUrl => string.IsNullOrEmpty(_baseAddress)
        ? $"/{RelativePath}"
        : $"{_baseAddress}/{RelativePath}";

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns true as availability is determined at request time.
    /// </remarks>
    public override bool IsAvailable => true;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns true as static assets can be loaded directly by URL.
    /// </remarks>
    public override bool SupportsDirectUrl => true;

    /// <inheritdoc/>
    public override async Task<byte[]?> GetDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(RelativePath, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to load static asset '{RelativePath}'. " +
                    $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}. " +
                    "Ensure the file exists in wwwroot and the path is correct.");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to fetch static asset '{RelativePath}': {ex.Message}. " +
                "Ensure the HttpClient base address is correctly configured.", ex);
        }
    }

    /// <inheritdoc/>
    public override Task<string?> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(FullUrl);
    }
}

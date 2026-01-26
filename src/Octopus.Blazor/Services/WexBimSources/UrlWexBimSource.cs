namespace Octopus.Blazor.Services.WexBimSources;

using Octopus.Blazor.Services.Abstractions;

/// <summary>
/// A WexBIM source that loads data from an HTTP/HTTPS URL.
/// <para>
/// This source supports direct URL loading, making it efficient for the viewer
/// to load the WexBIM directly without an intermediate byte array copy.
/// </para>
/// </summary>
/// <remarks>
/// Compatible with both Blazor WebAssembly and Blazor Server.
/// For WebAssembly, use URLs that are accessible from the browser (same-origin or CORS-enabled).
/// </remarks>
public class UrlWexBimSource : WexBimSourceBase
{
    private readonly HttpClient? _httpClient;
    private readonly Func<HttpClient>? _httpClientFactory;
    private readonly Dictionary<string, string> _headers;

    /// <summary>
    /// Initializes a new instance of the <see cref="UrlWexBimSource"/> class.
    /// </summary>
    /// <param name="url">The URL of the WexBIM file.</param>
    /// <param name="name">Optional display name. Defaults to the URL filename.</param>
    /// <param name="httpClient">Optional HttpClient for fetching data. Required for GetDataAsync.</param>
    /// <param name="headers">Optional headers to include in HTTP requests.</param>
    public UrlWexBimSource(
        string url,
        string? name = null,
        HttpClient? httpClient = null,
        IDictionary<string, string>? headers = null)
        : base(
            GenerateId("url", url ?? throw new ArgumentNullException(nameof(url))),
            name ?? GetFileNameFromUrl(url),
            WexBimSourceType.Url)
    {
        Url = url;
        _httpClient = httpClient;
        _headers = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UrlWexBimSource"/> class with a factory for HttpClient.
    /// </summary>
    /// <param name="url">The URL of the WexBIM file.</param>
    /// <param name="httpClientFactory">Factory function to create HttpClient instances.</param>
    /// <param name="name">Optional display name. Defaults to the URL filename.</param>
    /// <param name="headers">Optional headers to include in HTTP requests.</param>
    public UrlWexBimSource(
        string url,
        Func<HttpClient> httpClientFactory,
        string? name = null,
        IDictionary<string, string>? headers = null)
        : base(
            GenerateId("url", url ?? throw new ArgumentNullException(nameof(url))),
            name ?? GetFileNameFromUrl(url),
            WexBimSourceType.Url)
    {
        Url = url;
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _headers = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
    }

    /// <summary>
    /// Gets the URL of the WexBIM file.
    /// </summary>
    public string Url { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns true as URL availability is determined at request time.
    /// </remarks>
    public override bool IsAvailable => true;

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns true as URL sources support direct loading.
    /// </remarks>
    public override bool SupportsDirectUrl => true;

    /// <inheritdoc/>
    public override async Task<byte[]?> GetDataAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClient ?? _httpClientFactory?.Invoke();
        if (client == null)
        {
            throw new InvalidOperationException(
                "No HttpClient available. Provide an HttpClient or use GetUrlAsync for direct URL loading.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            foreach (var header in _headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to fetch WexBIM from URL '{Url}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public override Task<string?> GetUrlAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(Url);
    }

    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url, UriKind.RelativeOrAbsolute);
            if (uri.IsAbsoluteUri)
            {
                var segments = uri.Segments;
                if (segments.Length > 0)
                {
                    var lastSegment = segments[^1].TrimEnd('/');
                    if (!string.IsNullOrEmpty(lastSegment))
                    {
                        return Uri.UnescapeDataString(lastSegment);
                    }
                }
            }
            else
            {
                var lastSlash = url.LastIndexOfAny(['/', '\\']);
                if (lastSlash >= 0 && lastSlash < url.Length - 1)
                {
                    return url[(lastSlash + 1)..];
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return url;
    }
}

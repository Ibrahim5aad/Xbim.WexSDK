namespace Octopus.Api.Client;

/// <summary>
/// Factory for creating configured OctopusApiClient instances.
/// </summary>
public interface IOctopusClientFactory
{
    /// <summary>
    /// Creates a new OctopusApiClient instance.
    /// </summary>
    /// <returns>A configured IOctopusApiClient instance.</returns>
    IOctopusApiClient CreateClient();
}

/// <summary>
/// Default implementation of IOctopusClientFactory.
/// Creates OctopusApiClient instances with the configured HttpClient and base URL.
/// </summary>
public class OctopusClientFactory : IOctopusClientFactory
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a new OctopusClientFactory with the specified HttpClient and base URL.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use for API requests.</param>
    /// <param name="baseUrl">The base URL of the Octopus API.</param>
    public OctopusClientFactory(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
    }

    /// <inheritdoc />
    public IOctopusApiClient CreateClient()
    {
        return new OctopusApiClient(_baseUrl, _httpClient);
    }
}

/// <summary>
/// Configuration options for Octopus API client.
/// </summary>
public class OctopusClientOptions
{
    /// <summary>
    /// The base URL of the Octopus API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional token provider for authentication.
    /// When set, the client will automatically attach Bearer tokens to requests.
    /// </summary>
    public IAuthTokenProvider? TokenProvider { get; set; }

    /// <summary>
    /// Optional delegate for providing tokens.
    /// This is a convenience property that creates a DelegateTokenProvider internally.
    /// If TokenProvider is also set, this property is ignored.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? TokenFactory { get; set; }

    /// <summary>
    /// Gets the effective token provider based on configuration.
    /// </summary>
    internal IAuthTokenProvider? GetEffectiveTokenProvider()
    {
        if (TokenProvider != null)
            return TokenProvider;

        if (TokenFactory != null)
            return new DelegateTokenProvider(TokenFactory);

        return null;
    }
}

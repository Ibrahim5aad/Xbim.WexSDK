using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Octopus.Client;

/// <summary>
/// Extension methods for registering Octopus.Client services in DI containers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Octopus API client to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusClient(
        this IServiceCollection services,
        string baseUrl)
    {
        return services.AddOctopusClient(options =>
        {
            options.BaseUrl = baseUrl;
        });
    }

    /// <summary>
    /// Adds the Octopus API client to the service collection with the specified options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure client options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusClient(
        this IServiceCollection services,
        Action<OctopusClientOptions> configureOptions)
    {
        var options = new OctopusClientOptions();
        configureOptions(options);

        if (string.IsNullOrEmpty(options.BaseUrl))
            throw new ArgumentException("BaseUrl must be configured.", nameof(configureOptions));

        var tokenProvider = options.GetEffectiveTokenProvider();
        var baseUrl = options.BaseUrl;

        // Register the HttpClient with the auth handler if a token provider is configured
        var httpClientBuilder = services.AddHttpClient("OctopusApiClient", client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(2); // Allow longer operations
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
        .AddHttpMessageHandler(() =>
        {
            // Use the token provider if configured, otherwise use a no-op provider
            var provider = tokenProvider ?? new StaticTokenProvider(null);
            return new AuthorizationDelegatingHandler(provider);
        })
        // Configure resilience with longer timeouts for API operations
        .AddStandardResilienceHandler(options =>
        {
            // Increase attempt timeout for slower database operations during development
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(1);
            // Circuit breaker sampling duration must be at least 2x the attempt timeout
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
            // Increase total request timeout
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
        });

        // Register IOctopusApiClient with a factory that provides the baseUrl parameter
        services.TryAddTransient<IOctopusApiClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("OctopusApiClient");
            return new OctopusApiClient(baseUrl, httpClient);
        });

        // Register the token provider if provided
        if (tokenProvider != null)
        {
            services.TryAddSingleton(tokenProvider);
        }

        // Register the factory
        services.TryAddSingleton<IOctopusClientFactory>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("OctopusApiClient");
            return new OctopusClientFactory(httpClient, baseUrl);
        });

        return services;
    }

    /// <summary>
    /// Adds the Octopus API client with a token provider for authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API.</param>
    /// <param name="tokenProvider">The token provider for authentication.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusClient(
        this IServiceCollection services,
        string baseUrl,
        IAuthTokenProvider tokenProvider)
    {
        return services.AddOctopusClient(options =>
        {
            options.BaseUrl = baseUrl;
            options.TokenProvider = tokenProvider;
        });
    }

    /// <summary>
    /// Adds the Octopus API client with a token factory function for authentication.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API.</param>
    /// <param name="tokenFactory">A function that provides authentication tokens.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusClient(
        this IServiceCollection services,
        string baseUrl,
        Func<Task<string?>> tokenFactory)
    {
        return services.AddOctopusClient(options =>
        {
            options.BaseUrl = baseUrl;
            options.TokenFactory = _ => tokenFactory();
        });
    }

    /// <summary>
    /// Adds the Octopus API client with a token factory function that accepts a cancellation token.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API.</param>
    /// <param name="tokenFactory">A function that provides authentication tokens.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusClient(
        this IServiceCollection services,
        string baseUrl,
        Func<CancellationToken, Task<string?>> tokenFactory)
    {
        return services.AddOctopusClient(options =>
        {
            options.BaseUrl = baseUrl;
            options.TokenFactory = tokenFactory;
        });
    }
}

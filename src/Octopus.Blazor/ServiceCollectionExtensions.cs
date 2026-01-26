using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octopus.Blazor.Models;
using Octopus.Blazor.Services;
using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.Abstractions.Server;
using Octopus.Blazor.Services.Server;
using Octopus.Blazor.Services.Server.Guards;
using Octopus.Blazor.Services.WexBimSources;
using Octopus.Client;

namespace Octopus.Blazor;

/// <summary>
/// Extension methods for configuring Octopus.Blazor services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core Octopus.Blazor services for standalone viewer applications.
    /// <para>
    /// This registration is suitable for applications that:
    /// <list type="bullet">
    ///   <item>Load WexBIM files from local sources, static assets, or URLs</item>
    ///   <item>Do not require Octopus.Server connectivity</item>
    ///   <item>Do not need IFC file processing (use pre-converted WexBIM files)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Services registered:
    /// <list type="bullet">
    ///   <item><see cref="ThemeService"/> - Theme management (singleton)</item>
    ///   <item><see cref="IPropertyService"/> / <see cref="PropertyService"/> - Property aggregation (singleton)</item>
    ///   <item><see cref="IWexBimSourceProvider"/> / <see cref="WexBimSourceProvider"/> - WexBIM source management (singleton)</item>
    ///   <item><see cref="IfcHierarchyService"/> - Hierarchy generation (singleton)</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazorStandalone(this IServiceCollection services)
    {
        return services.AddOctopusBlazorStandalone(_ => { });
    }

    /// <summary>
    /// Adds core Octopus.Blazor services for standalone viewer applications with custom configuration.
    /// <para>
    /// This registration is suitable for applications that:
    /// <list type="bullet">
    ///   <item>Load WexBIM files from local sources, static assets, or URLs</item>
    ///   <item>Do not require Octopus.Server connectivity</item>
    ///   <item>Do not need IFC file processing (use pre-converted WexBIM files)</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="OctopusBlazorOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazorStandalone(
        this IServiceCollection services,
        Action<OctopusBlazorOptions> configure)
    {
        var options = new OctopusBlazorOptions();
        configure(options);

        // Store options for later use during source registration
        services.TryAddSingleton(options);

        // Register ThemeService with configured options
        var themeService = new ThemeService();
        themeService.SetTheme(options.InitialTheme);
        themeService.SetAccentColors(options.LightAccentColor, options.DarkAccentColor);
        themeService.SetBackgroundColors(options.LightBackgroundColor, options.DarkBackgroundColor);
        services.TryAddSingleton(themeService);

        // Register PropertyService as both interface and concrete type for backward compatibility
        services.TryAddSingleton<PropertyService>();
        services.TryAddSingleton<IPropertyService>(sp => sp.GetRequiredService<PropertyService>());

        // Register WexBimSourceProvider as both interface and concrete type
        services.TryAddSingleton<WexBimSourceProvider>();
        services.TryAddSingleton<IWexBimSourceProvider>(sp => sp.GetRequiredService<WexBimSourceProvider>());

        // Register IfcHierarchyService
        services.TryAddSingleton<IfcHierarchyService>();

        // Register configured sources if provided (URL and local file sources only - static assets need HttpClient)
        if (options.StandaloneSources != null)
        {
            services.AddSingleton<IWexBimSourceInitializer>(sp =>
                new StandaloneSourceInitializer(options.StandaloneSources));
        }

        // Register guard implementations for server-only services.
        // These throw ServerServiceNotConfiguredException with actionable messages when used,
        // preventing ambiguous null reference errors and guiding users to proper configuration.
        services.TryAddSingleton<IWorkspacesService, NotConfiguredWorkspacesService>();
        services.TryAddSingleton<IProjectsService, NotConfiguredProjectsService>();
        services.TryAddSingleton<IFilesService, NotConfiguredFilesService>();
        services.TryAddSingleton<IModelsService, NotConfiguredModelsService>();
        services.TryAddSingleton<IUsageService, NotConfiguredUsageService>();
        services.TryAddSingleton<IProcessingService, NotConfiguredProcessingService>();

        return services;
    }

    /// <summary>
    /// Configures pre-registered standalone WexBIM sources using an HttpClient.
    /// <para>
    /// Call this after <see cref="AddOctopusBlazorStandalone(IServiceCollection, Action{OctopusBlazorOptions})"/>
    /// to enable static asset sources that require HttpClient for loading.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="httpClientFactory">Factory function to create HttpClient instances.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection ConfigureStandaloneHttpClient(
        this IServiceCollection services,
        Func<IServiceProvider, HttpClient> httpClientFactory)
    {
        services.AddSingleton<IWexBimSourceInitializer>(sp =>
        {
            var options = sp.GetService<OctopusBlazorOptions>();
            if (options?.StandaloneSources == null)
            {
                return new NoOpSourceInitializer();
            }
            return new HttpClientSourceInitializer(options.StandaloneSources, httpClientFactory(sp));
        });

        return services;
    }

    /// <summary>
    /// Adds Octopus.Blazor services for Blazor Server applications with IFC processing capabilities.
    /// <para>
    /// This registration extends <see cref="AddOctopusBlazorStandalone"/> with:
    /// <list type="bullet">
    ///   <item><see cref="IIfcModelService"/> / <see cref="IfcModelService"/> - Server-side IFC to WexBIM conversion</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Note:</strong> <see cref="IfcModelService"/> uses native xBIM libraries and only works
    /// in server-side scenarios (Blazor Server, ASP.NET Core). It is not compatible with Blazor WebAssembly.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazorServer(this IServiceCollection services)
    {
        return services.AddOctopusBlazorServer(_ => { });
    }

    /// <summary>
    /// Adds Octopus.Blazor services for Blazor Server applications with IFC processing capabilities.
    /// <para>
    /// This registration extends <see cref="AddOctopusBlazorStandalone"/> with:
    /// <list type="bullet">
    ///   <item><see cref="IIfcModelService"/> / <see cref="IfcModelService"/> - Server-side IFC to WexBIM conversion</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Note:</strong> <see cref="IfcModelService"/> uses native xBIM libraries and only works
    /// in server-side scenarios (Blazor Server, ASP.NET Core). It is not compatible with Blazor WebAssembly.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="OctopusBlazorOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazorServer(
        this IServiceCollection services,
        Action<OctopusBlazorOptions> configure)
    {
        // Add standalone services first
        services.AddOctopusBlazorStandalone(configure);

        // Add server-specific services as both interface and concrete type for backward compatibility
        services.TryAddSingleton<IfcModelService>();
        services.TryAddSingleton<IIfcModelService>(sp => sp.GetRequiredService<IfcModelService>());

        return services;
    }

    /// <summary>
    /// Adds Octopus.Blazor services with default configuration.
    /// <para>
    /// This is an alias for <see cref="AddOctopusBlazorStandalone()"/> for backward compatibility.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazor(this IServiceCollection services)
    {
        return services.AddOctopusBlazorStandalone();
    }

    /// <summary>
    /// Adds Octopus.Blazor services with custom configuration.
    /// <para>
    /// This is an alias for <see cref="AddOctopusBlazorStandalone(IServiceCollection, Action{OctopusBlazorOptions})"/>
    /// for backward compatibility.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="OctopusBlazorOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazor(
        this IServiceCollection services,
        Action<OctopusBlazorOptions> configure)
    {
        return services.AddOctopusBlazorStandalone(configure);
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity for full application functionality.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties, WexBIM sources)</item>
    ///   <item>Server-backed services for workspaces, projects, files, models, usage, and processing</item>
    ///   <item>Octopus API client with optional authentication</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Prerequisites:</strong>
    /// <list type="bullet">
    ///   <item>A running Octopus.Server instance</item>
    ///   <item>Valid base URL configuration</item>
    ///   <item>Optional: Authentication token provider for secured endpoints</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API server.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if baseUrl is null or empty.</exception>
    public static IServiceCollection AddOctopusBlazorServerConnected(
        this IServiceCollection services,
        string baseUrl)
    {
        return services.AddOctopusBlazorServerConnected(baseUrl, _ => { });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and custom Blazor options.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties, WexBIM sources)</item>
    ///   <item>Server-backed services for workspaces, projects, files, models, usage, and processing</item>
    ///   <item>Octopus API client with optional authentication</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API server.</param>
    /// <param name="configureBlazor">An action to configure the <see cref="OctopusBlazorOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if baseUrl is null or empty.</exception>
    public static IServiceCollection AddOctopusBlazorServerConnected(
        this IServiceCollection services,
        string baseUrl,
        Action<OctopusBlazorOptions> configureBlazor)
    {
        return services.AddOctopusBlazorServerConnected(baseUrl, configureBlazor, _ => { });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and authentication.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties, WexBIM sources)</item>
    ///   <item>Server-backed services for workspaces, projects, files, models, usage, and processing</item>
    ///   <item>Octopus API client with token-based authentication</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API server.</param>
    /// <param name="tokenProvider">The token provider for authentication.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if baseUrl is null or empty.</exception>
    public static IServiceCollection AddOctopusBlazorServerConnected(
        this IServiceCollection services,
        string baseUrl,
        IAuthTokenProvider tokenProvider)
    {
        return services.AddOctopusBlazorServerConnected(baseUrl, _ => { }, options =>
        {
            options.TokenProvider = tokenProvider;
        });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and a token factory function.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties, WexBIM sources)</item>
    ///   <item>Server-backed services for workspaces, projects, files, models, usage, and processing</item>
    ///   <item>Octopus API client with token-based authentication</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API server.</param>
    /// <param name="tokenFactory">A function that provides authentication tokens.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if baseUrl is null or empty.</exception>
    public static IServiceCollection AddOctopusBlazorServerConnected(
        this IServiceCollection services,
        string baseUrl,
        Func<Task<string?>> tokenFactory)
    {
        return services.AddOctopusBlazorServerConnected(baseUrl, _ => { }, options =>
        {
            options.TokenFactory = _ => tokenFactory();
        });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and full configuration control.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties, WexBIM sources)</item>
    ///   <item>Server-backed services for workspaces, projects, files, models, usage, and processing</item>
    ///   <item>Octopus API client with configurable authentication</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the Octopus API server.</param>
    /// <param name="configureBlazor">An action to configure the <see cref="OctopusBlazorOptions"/>.</param>
    /// <param name="configureClient">An action to configure the <see cref="OctopusClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown if baseUrl is null or empty.</exception>
    public static IServiceCollection AddOctopusBlazorServerConnected(
        this IServiceCollection services,
        string baseUrl,
        Action<OctopusBlazorOptions> configureBlazor,
        Action<OctopusClientOptions> configureClient)
    {
        if (string.IsNullOrEmpty(baseUrl))
            throw new ArgumentException("BaseUrl must be provided.", nameof(baseUrl));

        // Add standalone services first
        services.AddOctopusBlazorStandalone(configureBlazor);

        // Add the Octopus API client
        services.AddOctopusClient(options =>
        {
            options.BaseUrl = baseUrl;
            configureClient(options);
        });

        // Register server-backed services, replacing any guard implementations from standalone mode.
        // Using Replace ensures that the real implementations override the guards that throw
        // ServerServiceNotConfiguredException.
        services.Replace(ServiceDescriptor.Singleton<IWorkspacesService, WorkspacesService>());
        services.Replace(ServiceDescriptor.Singleton<IProjectsService, ProjectsService>());
        services.Replace(ServiceDescriptor.Singleton<IFilesService, FilesService>());
        services.Replace(ServiceDescriptor.Singleton<IModelsService, ModelsService>());
        services.Replace(ServiceDescriptor.Singleton<IUsageService, UsageService>());
        services.Replace(ServiceDescriptor.Singleton<IProcessingService, ProcessingService>());

        return services;
    }
}

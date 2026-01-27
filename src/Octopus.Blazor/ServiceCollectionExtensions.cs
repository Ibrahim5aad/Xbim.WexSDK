using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octopus.Blazor.Models;
using Octopus.Blazor.Services;
using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.Abstractions.Server;
using Octopus.Blazor.Services.Server;
using Octopus.Blazor.Services.Server.Guards;
using Octopus.Api.Client;

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
    ///   <item>Load WexBIM files via FileLoaderPanel (from URLs, static assets, or local files)</item>
    ///   <item>Do not require Octopus.Server connectivity</item>
    ///   <item>Do not need IFC file processing (use pre-converted WexBIM files)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Services registered:
    /// <list type="bullet">
    ///   <item><see cref="ThemeService"/> - Theme management (singleton)</item>
    ///   <item><see cref="IPropertyService"/> / <see cref="PropertyService"/> - Property aggregation (singleton)</item>
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
    ///   <item>Load WexBIM files via FileLoaderPanel (from URLs, static assets, or local files)</item>
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

        // Store options for later use
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

        // Register IfcHierarchyService
        services.TryAddSingleton<IfcHierarchyService>();

        // Register the standalone hosting mode provider
        services.TryAddSingleton<IOctopusHostingModeProvider, StandaloneHostingModeProvider>();

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
    /// Adds Octopus.Blazor services for standalone viewer applications using configuration binding.
    /// <para>
    /// This method binds configuration from the "Octopus:Standalone" section in appsettings.json.
    /// </para>
    /// <para>
    /// Example appsettings.json:
    /// <code>
    /// {
    ///   "Octopus": {
    ///     "Standalone": {
    ///       "Theme": {
    ///         "InitialTheme": "Dark",
    ///         "LightAccentColor": "#0969da",
    ///         "DarkAccentColor": "#1e7e34"
    ///       },
    ///       "FileLoaderPanel": {
    ///         "AllowIfcFiles": true,
    ///         "AutoCloseOnLoad": true,
    ///         "DemoModels": [
    ///           { "Name": "Sample House", "Path": "models/SampleHouse.wexbim" }
    ///         ]
    ///       }
    ///     }
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazorStandalone(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(OctopusStandaloneOptions.SectionName);
        var standaloneOptions = new OctopusStandaloneOptions();
        section.Bind(standaloneOptions);

        // Store standalone options for retrieval
        services.TryAddSingleton(standaloneOptions);

        return services.AddOctopusBlazorStandalone(options =>
        {
            // Apply theme settings
            options.InitialTheme = standaloneOptions.Theme.GetViewerTheme();
            options.LightAccentColor = standaloneOptions.Theme.LightAccentColor;
            options.DarkAccentColor = standaloneOptions.Theme.DarkAccentColor;
            options.LightBackgroundColor = standaloneOptions.Theme.LightBackgroundColor;
            options.DarkBackgroundColor = standaloneOptions.Theme.DarkBackgroundColor;

            // Apply FileLoaderPanel settings
            options.FileLoaderPanel = standaloneOptions.FileLoaderPanel;
        });
    }

    /// <summary>
    /// Adds Octopus.Blazor services for Blazor Server applications using configuration binding.
    /// <para>
    /// This method binds configuration from the "Octopus:Standalone" section in appsettings.json,
    /// and adds IFC processing capabilities for server-side scenarios.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOctopusBlazorServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(OctopusStandaloneOptions.SectionName);
        var standaloneOptions = new OctopusStandaloneOptions();
        section.Bind(standaloneOptions);

        // Store standalone options for retrieval
        services.TryAddSingleton(standaloneOptions);

        return services.AddOctopusBlazorServer(options =>
        {
            // Apply theme settings
            options.InitialTheme = standaloneOptions.Theme.GetViewerTheme();
            options.LightAccentColor = standaloneOptions.Theme.LightAccentColor;
            options.DarkAccentColor = standaloneOptions.Theme.DarkAccentColor;
            options.LightBackgroundColor = standaloneOptions.Theme.LightBackgroundColor;
            options.DarkBackgroundColor = standaloneOptions.Theme.DarkBackgroundColor;

            // Apply FileLoaderPanel settings
            options.FileLoaderPanel = standaloneOptions.FileLoaderPanel;
        });
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
    ///   <item>All standalone services (themes, properties)</item>
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
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
        this IServiceCollection services,
        string baseUrl)
    {
        return services.AddOctopusBlazorPlatformConnected(baseUrl, _ => { });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and custom Blazor options.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties)</item>
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
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
        this IServiceCollection services,
        string baseUrl,
        Action<OctopusBlazorOptions> configureBlazor)
    {
        return services.AddOctopusBlazorPlatformConnected(baseUrl, configureBlazor, _ => { });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and authentication.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties)</item>
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
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
        this IServiceCollection services,
        string baseUrl,
        IAuthTokenProvider tokenProvider)
    {
        return services.AddOctopusBlazorPlatformConnected(baseUrl, _ => { }, options =>
        {
            options.TokenProvider = tokenProvider;
        });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and a token factory function.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties)</item>
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
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
        this IServiceCollection services,
        string baseUrl,
        Func<Task<string?>> tokenFactory)
    {
        return services.AddOctopusBlazorPlatformConnected(baseUrl, _ => { }, options =>
        {
            options.TokenFactory = _ => tokenFactory();
        });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity and full configuration control.
    /// <para>
    /// This registration includes:
    /// <list type="bullet">
    ///   <item>All standalone services (themes, properties)</item>
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
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
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

        // Replace the hosting mode provider to indicate PlatformConnected mode.
        // This enables components to detect the mode and adjust behavior accordingly.
        services.Replace(ServiceDescriptor.Singleton<IOctopusHostingModeProvider, PlatformConnectedHostingModeProvider>());

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

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity using configuration binding.
    /// <para>
    /// This method binds configuration from the "Octopus:Server" section in appsettings.json.
    /// </para>
    /// <para>
    /// Example appsettings.json:
    /// <code>
    /// {
    ///   "Octopus": {
    ///     "Server": {
    ///       "BaseUrl": "https://api.octopus.example.com",
    ///       "RequireAuthentication": true,
    ///       "TimeoutSeconds": 30
    ///     }
    ///   }
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// For authentication, provide a token factory via the <paramref name="configureClient"/> action,
    /// or implement <see cref="IAuthTokenProvider"/> and register it in the service collection before calling this method.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when server configuration is missing or invalid.</exception>
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddOctopusBlazorPlatformConnected(configuration, _ => { }, _ => { });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity using configuration binding and custom Blazor options.
    /// <para>
    /// This method binds server configuration from the "Octopus:Server" section and standalone configuration
    /// from the "Octopus:Standalone" section in appsettings.json.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="configureBlazor">An action to configure additional Blazor options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when server configuration is missing or invalid.</exception>
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OctopusBlazorOptions> configureBlazor)
    {
        return services.AddOctopusBlazorPlatformConnected(configuration, configureBlazor, _ => { });
    }

    /// <summary>
    /// Adds Octopus.Blazor services with server connectivity using configuration binding and full control.
    /// <para>
    /// This method binds server configuration from the "Octopus:Server" section and standalone configuration
    /// from the "Octopus:Standalone" section in appsettings.json.
    /// </para>
    /// <para>
    /// Example appsettings.json:
    /// <code>
    /// {
    ///   "Octopus": {
    ///     "Server": {
    ///       "BaseUrl": "https://api.octopus.example.com",
    ///       "RequireAuthentication": true,
    ///       "TimeoutSeconds": 30
    ///     },
    ///     "Standalone": {
    ///       "Theme": { "InitialTheme": "Dark" },
    ///       "FileLoaderPanel": { "AllowIfcFiles": false }
    ///     }
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="configureBlazor">An action to configure additional Blazor options.</param>
    /// <param name="configureClient">An action to configure the Octopus client options (e.g., authentication).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when server configuration is missing or invalid.</exception>
    public static IServiceCollection AddOctopusBlazorPlatformConnected(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OctopusBlazorOptions> configureBlazor,
        Action<OctopusClientOptions> configureClient)
    {
        // Bind and validate server options
        var serverSection = configuration.GetSection(OctopusServerOptions.SectionName);
        var serverOptions = new OctopusServerOptions();
        serverSection.Bind(serverOptions);

        // Validate configuration at startup - fail fast with actionable message
        serverOptions.Validate();

        // Store server options for retrieval by components
        services.TryAddSingleton(serverOptions);

        // Bind standalone options for theme and FileLoaderPanel configuration
        var standaloneSection = configuration.GetSection(OctopusStandaloneOptions.SectionName);
        var standaloneOptions = new OctopusStandaloneOptions();
        standaloneSection.Bind(standaloneOptions);
        services.TryAddSingleton(standaloneOptions);

        // Configure Blazor options from configuration + custom action
        Action<OctopusBlazorOptions> combinedConfigure = options =>
        {
            // Apply theme settings from configuration
            options.InitialTheme = standaloneOptions.Theme.GetViewerTheme();
            options.LightAccentColor = standaloneOptions.Theme.LightAccentColor;
            options.DarkAccentColor = standaloneOptions.Theme.DarkAccentColor;
            options.LightBackgroundColor = standaloneOptions.Theme.LightBackgroundColor;
            options.DarkBackgroundColor = standaloneOptions.Theme.DarkBackgroundColor;

            // Apply FileLoaderPanel settings
            options.FileLoaderPanel = standaloneOptions.FileLoaderPanel;

            // Apply any custom configuration
            configureBlazor(options);
        };

        // Configure client options from configuration + custom action
        Action<OctopusClientOptions> combinedClientConfigure = options =>
        {
            options.BaseUrl = serverOptions.BaseUrl!;
            // Note: TimeoutSeconds is stored in OctopusServerOptions for reference,
            // but HttpClient timeout is configured by the host (AddOctopusClient handles defaults)

            // Apply any custom configuration
            configureClient(options);
        };

        return services.AddOctopusBlazorPlatformConnected(
            serverOptions.BaseUrl!,
            combinedConfigure,
            combinedClientConfigure);
    }
}

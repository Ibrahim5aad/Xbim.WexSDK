using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octopus.Blazor.Models;
using Octopus.Blazor.Services;
using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.WexBimSources;

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
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xbim.WexServer.Abstractions.Storage;

namespace Xbim.WexServer.Storage.LocalDisk;

/// <summary>
/// Extension methods for registering Local Disk Storage provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Local Disk Storage provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration section for Local Disk Storage options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLocalDiskStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LocalDiskStorageOptions>(configuration);
        services.AddSingleton<IStorageProvider, LocalDiskStorageProvider>();
        return services;
    }

    /// <summary>
    /// Adds Local Disk Storage provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLocalDiskStorage(
        this IServiceCollection services,
        Action<LocalDiskStorageOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IStorageProvider, LocalDiskStorageProvider>();
        return services;
    }

    /// <summary>
    /// Adds Local Disk Storage provider with default options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="basePath">The base directory path for storage.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLocalDiskStorage(
        this IServiceCollection services,
        string basePath = "Xbim-storage")
    {
        return services.AddLocalDiskStorage(options => options.BasePath = basePath);
    }
}

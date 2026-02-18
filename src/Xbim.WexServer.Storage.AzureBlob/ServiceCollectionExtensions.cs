using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xbim.WexServer.Abstractions.Storage;

namespace Xbim.WexServer.Storage.AzureBlob;

/// <summary>
/// Extension methods for registering Azure Blob Storage provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Azure Blob Storage provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration section for Azure Blob Storage options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureBlobStorageOptions>(configuration);
        services.AddSingleton<IStorageProvider, AzureBlobStorageProvider>();
        return services;
    }

    /// <summary>
    /// Adds Azure Blob Storage provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureBlobStorage(
        this IServiceCollection services,
        Action<AzureBlobStorageOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IStorageProvider, AzureBlobStorageProvider>();
        return services;
    }
}

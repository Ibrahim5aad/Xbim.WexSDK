using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xbim.WexServer.Abstractions.Storage;

namespace Xbim.WexServer.App.HealthChecks;

/// <summary>
/// Health check that verifies the storage provider is accessible and functioning.
/// </summary>
public class StorageProviderHealthCheck : IHealthCheck
{
    private readonly IStorageProvider _storageProvider;

    public StorageProviderHealthCheck(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _storageProvider.CheckHealthAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["provider"] = _storageProvider.ProviderId
            };

            if (result.Data != null)
            {
                foreach (var kvp in result.Data)
                {
                    data[kvp.Key] = kvp.Value;
                }
            }

            if (result.IsHealthy)
            {
                return HealthCheckResult.Healthy(
                    result.Message ?? $"{_storageProvider.ProviderId} storage is healthy",
                    data);
            }

            return HealthCheckResult.Unhealthy(
                result.Message ?? $"{_storageProvider.ProviderId} storage is unhealthy",
                data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Storage provider health check failed: {ex.Message}",
                ex,
                new Dictionary<string, object>
                {
                    ["provider"] = _storageProvider.ProviderId,
                    ["exception"] = ex.GetType().Name
                });
        }
    }
}

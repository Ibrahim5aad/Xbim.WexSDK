using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.App.HealthChecks;

namespace Xbim.WexServer.App.Tests.HealthChecks;

public class StorageProviderHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenStorageHealthy_ReturnsHealthy()
    {
        // Arrange
        var storageProvider = new MockStorageProvider(isHealthy: true, message: "Storage is working");
        var healthCheck = new StorageProviderHealthCheck(storageProvider);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("storage", healthCheck, null, null)
        };

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("Storage is working", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal("MockStorage", result.Data["provider"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenStorageUnhealthy_ReturnsUnhealthy()
    {
        // Arrange
        var storageProvider = new MockStorageProvider(isHealthy: false, message: "Cannot connect to storage");
        var healthCheck = new StorageProviderHealthCheck(storageProvider);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("storage", healthCheck, null, null)
        };

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Cannot connect to storage", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenStorageThrowsException_ReturnsUnhealthy()
    {
        // Arrange
        var storageProvider = new ThrowingStorageProvider();
        var healthCheck = new StorageProviderHealthCheck(storageProvider);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("storage", healthCheck, null, null)
        };

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("health check failed", result.Description);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesProviderIdInData()
    {
        // Arrange
        var storageProvider = new MockStorageProvider(isHealthy: true, message: "OK");
        var healthCheck = new StorageProviderHealthCheck(storageProvider);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("storage", healthCheck, null, null)
        };

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("provider"));
        Assert.Equal("MockStorage", result.Data["provider"]);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesStorageDataInResult()
    {
        // Arrange
        var additionalData = new Dictionary<string, object>
        {
            ["freeSpace"] = 1000000L,
            ["region"] = "eastus"
        };
        var storageProvider = new MockStorageProvider(isHealthy: true, message: "OK", additionalData);
        var healthCheck = new StorageProviderHealthCheck(storageProvider);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("storage", healthCheck, null, null)
        };

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal(1000000L, result.Data["freeSpace"]);
        Assert.Equal("eastus", result.Data["region"]);
    }

    #region Mock Implementations

    private class MockStorageProvider : IStorageProvider
    {
        private readonly bool _isHealthy;
        private readonly string _message;
        private readonly IReadOnlyDictionary<string, object>? _data;

        public MockStorageProvider(bool isHealthy, string message, IReadOnlyDictionary<string, object>? data = null)
        {
            _isHealthy = isHealthy;
            _message = message;
            _data = data;
        }

        public string ProviderId => "MockStorage";
        public bool SupportsDirectUpload => false;

        public Task<StorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            return _isHealthy
                ? Task.FromResult(StorageHealthResult.Healthy(_message, _data))
                : Task.FromResult(StorageHealthResult.Unhealthy(_message, _data));
        }

        public Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken cancellationToken = default)
            => Task.FromResult(key);

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(null);

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<long?>(null);

        public Task<string?> GenerateUploadSasUrlAsync(string key, string? contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private class ThrowingStorageProvider : IStorageProvider
    {
        public string ProviderId => "ThrowingStorage";
        public bool SupportsDirectUpload => false;

        public Task<StorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated storage failure");

        public Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string?> GenerateUploadSasUrlAsync(string key, string? contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    #endregion
}

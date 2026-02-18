using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.Tests.Endpoints;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.Tests.HealthChecks;

public class HealthCheckTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryStorageProvider _storageProvider;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public HealthCheckTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new TestInMemoryStorageProvider();
        _processingQueue = new TestInMemoryProcessingQueue();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Environment", "Testing");

                builder.ConfigureServices(services =>
                {
                    // Replace the DbContext with an in-memory database
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<XbimDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContext<XbimDbContext>(options =>
                        options.UseInMemoryDatabase(_testDbName));

                    // Replace IStorageProvider with in-memory implementation
                    services.RemoveAll(typeof(IStorageProvider));
                    services.AddSingleton<IStorageProvider>(_storageProvider);

                    // Replace IProcessingQueue with in-memory implementation
                    services.RemoveAll(typeof(IProcessingQueue));
                    services.AddSingleton<IProcessingQueue>(_processingQueue);
                });
            });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task HealthzEndpoint_ReturnsHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", content.ToLowerInvariant());
    }

    [Fact]
    public async Task HealthzEndpoint_ReturnsJsonContent()
    {
        // Act
        var response = await _client.GetAsync("/healthz");

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task HealthzEndpoint_ContainsStatusField()
    {
        // Act
        var response = await _client.GetAsync("/healthz");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("status", out var statusElement));
        Assert.Equal("healthy", statusElement.GetString());
    }

    [Fact]
    public async Task HealthzEndpoint_ContainsTotalDuration()
    {
        // Act
        var response = await _client.GetAsync("/healthz");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("totalDuration", out var durationElement));
        Assert.True(durationElement.GetDouble() >= 0);
    }

    [Fact]
    public async Task HealthzEndpoint_IncludesChecksArray()
    {
        // Act
        var response = await _client.GetAsync("/healthz");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("checks", out var checksElement));
        Assert.Equal(JsonValueKind.Array, checksElement.ValueKind);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// Test storage provider that can be configured to return healthy or unhealthy status.
    /// </summary>
    private class TestInMemoryStorageProvider : IStorageProvider
    {
        public string ProviderId => "InMemory";
        public bool SupportsDirectUpload => false;

        public bool IsHealthy { get; set; } = true;
        public string HealthMessage { get; set; } = "Test storage is healthy";

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

        public Task<StorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            return IsHealthy
                ? Task.FromResult(StorageHealthResult.Healthy(HealthMessage))
                : Task.FromResult(StorageHealthResult.Unhealthy(HealthMessage));
        }
    }
}

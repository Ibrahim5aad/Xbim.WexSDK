using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.RateLimiting;
using Xbim.WexServer.Tests.Endpoints;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.Tests.RateLimiting;

/// <summary>
/// Tests for rate limiting on upload endpoints.
/// </summary>
public class RateLimitingTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly InMemoryStorageProvider _storageProvider;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public RateLimitingTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProvider();
        _processingQueue = new TestInMemoryProcessingQueue();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove ALL DbContext-related services
                services.RemoveAll(typeof(DbContextOptions<XbimDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(XbimDbContext));

                // Remove storage provider and add in-memory one
                services.RemoveAll(typeof(IStorageProvider));
                services.AddSingleton<IStorageProvider>(_storageProvider);

                // Remove processing queue and add in-memory one
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

                // Add in-memory database for testing
                services.AddDbContext<XbimDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDbName);
                });

                // Re-configure rate limiting with strict test values
                // Remove all rate limiter related registrations first
                var rateLimiterDescriptors = services
                    .Where(d => d.ServiceType.FullName?.Contains("RateLimiter") == true ||
                                d.ServiceType.FullName?.Contains("RateLimiting") == true ||
                                d.ImplementationType?.FullName?.Contains("RateLimiter") == true ||
                                d.ImplementationType?.FullName?.Contains("RateLimiting") == true)
                    .ToList();
                foreach (var descriptor in rateLimiterDescriptors)
                {
                    services.Remove(descriptor);
                }

                // Also remove the options
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.RemoveAll<IPostConfigureOptions<RateLimiterOptions>>();

                // Add rate limiter with test-specific limits
                services.AddRateLimiter(limiterOptions =>
                {
                    limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                    limiterOptions.OnRejected = async (context, cancellationToken) =>
                    {
                        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        context.HttpContext.Response.ContentType = "application/json";
                        context.HttpContext.Response.Headers.RetryAfter = "60";
                        await context.HttpContext.Response.WriteAsJsonAsync(new
                        {
                            error = "Too Many Requests",
                            message = "Rate limit exceeded. Please try again later.",
                            retryAfterSeconds = 60
                        }, cancellationToken);
                    };

                    // Strict limits for testing: 3 reserve, 2 content, 2 commit
                    limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadReserve, limiter =>
                    {
                        limiter.PermitLimit = 3;
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                    limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadContent, limiter =>
                    {
                        limiter.PermitLimit = 2;
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                    limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadCommit, limiter =>
                    {
                        limiter.PermitLimit = 2;
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<WorkspaceDto> CreateWorkspaceAsync(string name = "Test Workspace")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    private async Task<ProjectDto> CreateProjectAsync(Guid workspaceId, string name = "Test Project")
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/projects",
            new CreateProjectRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    #region Upload Reserve Rate Limiting Tests

    [Fact]
    public async Task ReserveUpload_ReturnsOk_WithinRateLimit()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest { FileName = "test.ifc" };

        // Act - make 3 requests (within limit)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 3; i++)
        {
            responses.Add(await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads", request));
        }

        // Assert - all should succeed
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
    }

    [Fact]
    public async Task ReserveUpload_Returns429_WhenRateLimitExceeded()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest { FileName = "test.ifc" };

        // Act - make 4 requests (exceeds limit of 3)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 4; i++)
        {
            responses.Add(await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads", request));
        }

        // Assert - first 3 should succeed, 4th should be rate limited
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[2].StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[3].StatusCode);
    }

    [Fact]
    public async Task ReserveUpload_Returns429Response_WithRetryAfterHeader()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest { FileName = "test.ifc" };

        // Exhaust the rate limit
        for (int i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/files/uploads", request);
        }

        // Act - trigger rate limit
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads", request);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));

        var content = await response.Content.ReadFromJsonAsync<RateLimitErrorResponse>();
        Assert.NotNull(content);
        Assert.Equal("Too Many Requests", content.Error);
        Assert.Contains("Rate limit exceeded", content.Message);
        Assert.True(content.RetryAfterSeconds > 0);
    }

    #endregion

    #region Upload Content Rate Limiting Tests

    [Fact]
    public async Task UploadContent_ReturnsOk_WithinRateLimit()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var sessions = new List<Guid>();
        for (int i = 0; i < 2; i++)
        {
            var reserveResponse = await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads",
                new ReserveUploadRequest { FileName = $"test{i}.ifc" });
            var reserved = await reserveResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();
            sessions.Add(reserved!.Session.Id);
        }

        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00 };

        // Act - make 2 content upload requests (within limit)
        var responses = new List<HttpResponseMessage>();
        foreach (var sessionId in sessions)
        {
            var multipartContent = CreateFileContent("test.ifc", fileContent);
            responses.Add(await _client.PostAsync(
                $"/api/v1/projects/{project.Id}/files/uploads/{sessionId}/content",
                multipartContent));
        }

        // Assert - all should succeed
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task UploadContent_Returns429_WhenRateLimitExceeded()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Create 3 sessions
        var sessions = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            var reserveResponse = await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads",
                new ReserveUploadRequest { FileName = $"test{i}.ifc" });
            var reserved = await reserveResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();
            sessions.Add(reserved!.Session.Id);
        }

        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00 };

        // Act - make 3 content upload requests (exceeds limit of 2)
        var responses = new List<HttpResponseMessage>();
        foreach (var sessionId in sessions)
        {
            var multipartContent = CreateFileContent("test.ifc", fileContent);
            responses.Add(await _client.PostAsync(
                $"/api/v1/projects/{project.Id}/files/uploads/{sessionId}/content",
                multipartContent));
        }

        // Assert - first 2 should succeed, 3rd should be rate limited
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[2].StatusCode);
    }

    #endregion

    #region Upload Commit Rate Limiting Tests

    [Fact]
    public async Task CommitUpload_ReturnsOk_WithinRateLimit()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00 };

        var sessions = new List<Guid>();
        for (int i = 0; i < 2; i++)
        {
            // Reserve and upload
            var reserveResponse = await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads",
                new ReserveUploadRequest { FileName = $"test{i}.ifc" });
            var reserved = await reserveResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();
            sessions.Add(reserved!.Session.Id);

            var multipartContent = CreateFileContent($"test{i}.ifc", fileContent);
            await _client.PostAsync(
                $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
                multipartContent);
        }

        // Act - make 2 commit requests (within limit)
        var responses = new List<HttpResponseMessage>();
        foreach (var sessionId in sessions)
        {
            responses.Add(await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads/{sessionId}/commit",
                new CommitUploadRequest()));
        }

        // Assert - all should succeed
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task CommitUpload_Returns429_WhenRateLimitExceeded()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00 };

        // Create 3 sessions with content
        var sessions = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            // Reserve and upload
            var reserveResponse = await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads",
                new ReserveUploadRequest { FileName = $"test{i}.ifc" });
            var reserved = await reserveResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();
            sessions.Add(reserved!.Session.Id);

            var multipartContent = CreateFileContent($"test{i}.ifc", fileContent);
            await _client.PostAsync(
                $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
                multipartContent);
        }

        // Act - make 3 commit requests (exceeds limit of 2)
        var responses = new List<HttpResponseMessage>();
        foreach (var sessionId in sessions)
        {
            responses.Add(await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads/{sessionId}/commit",
                new CommitUploadRequest()));
        }

        // Assert - first 2 should succeed, 3rd should be rate limited
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[2].StatusCode);
    }

    #endregion

    #region Helper Methods

    private static MultipartFormDataContent CreateFileContent(string fileName, byte[] content)
    {
        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        multipartContent.Add(fileContent, "file", fileName);
        return multipartContent;
    }

    #endregion

    #region Response DTOs

    private record RateLimitErrorResponse
    {
        public string Error { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public int RetryAfterSeconds { get; init; }
    }

    #endregion
}

/// <summary>
/// Tests for rate limiting when disabled.
/// </summary>
public class RateLimitingDisabledTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly InMemoryStorageProvider _storageProvider;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public RateLimitingDisabledTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProvider();
        _processingQueue = new TestInMemoryProcessingQueue();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<XbimDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(XbimDbContext));
                services.RemoveAll(typeof(IStorageProvider));
                services.AddSingleton<IStorageProvider>(_storageProvider);
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);
                services.AddDbContext<XbimDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDbName);
                });

                // "Disable" rate limiting by using very high limits
                var rateLimiterDescriptors = services
                    .Where(d => d.ServiceType.FullName?.Contains("RateLimiter") == true ||
                                d.ServiceType.FullName?.Contains("RateLimiting") == true ||
                                d.ImplementationType?.FullName?.Contains("RateLimiter") == true ||
                                d.ImplementationType?.FullName?.Contains("RateLimiting") == true)
                    .ToList();
                foreach (var descriptor in rateLimiterDescriptors)
                {
                    services.Remove(descriptor);
                }
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.RemoveAll<IPostConfigureOptions<RateLimiterOptions>>();

                // Add rate limiter with very high limits (effectively disabled)
                services.AddRateLimiter(limiterOptions =>
                {
                    limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                    // Very high limits for "disabled" testing
                    limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadReserve, limiter =>
                    {
                        limiter.PermitLimit = 10000;
                        limiter.Window = TimeSpan.FromSeconds(1);
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                    limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadContent, limiter =>
                    {
                        limiter.PermitLimit = 10000;
                        limiter.Window = TimeSpan.FromSeconds(1);
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                    limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadCommit, limiter =>
                    {
                        limiter.PermitLimit = 10000;
                        limiter.Window = TimeSpan.FromSeconds(1);
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<WorkspaceDto> CreateWorkspaceAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = "Test Workspace" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    private async Task<ProjectDto> CreateProjectAsync(Guid workspaceId)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/projects",
            new CreateProjectRequest { Name = "Test Project" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    [Fact]
    public async Task ReserveUpload_AllowsUnlimitedRequests_WhenRateLimitingDisabled()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest { FileName = "test.ifc" };

        // Act - make many requests
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 10; i++)
        {
            responses.Add(await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/files/uploads", request));
        }

        // Assert - all should succeed
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
    }
}

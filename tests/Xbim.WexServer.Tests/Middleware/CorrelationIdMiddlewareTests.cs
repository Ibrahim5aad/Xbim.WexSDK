using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Middleware;
using Xbim.WexServer.Tests.Endpoints;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.Tests.Middleware;

public class CorrelationIdMiddlewareTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public CorrelationIdMiddlewareTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
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

                // Remove processing queue and add test implementation
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

                // Add in-memory database for testing
                services.AddDbContext<XbimDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDbName);
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

    [Fact]
    public async Task Response_ContainsCorrelationIdHeader_WhenNoCorrelationIdProvided()
    {
        // Act
        var response = await _client.GetAsync("/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.CorrelationIdHeader),
            $"Response should contain {CorrelationIdMiddleware.CorrelationIdHeader} header");

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.CorrelationIdHeader).FirstOrDefault();
        Assert.NotNull(correlationId);
        Assert.True(Guid.TryParse(correlationId, out _), "Correlation ID should be a valid GUID");
    }

    [Fact]
    public async Task Response_ContainsRequestIdHeader_WhenNoCorrelationIdProvided()
    {
        // Act
        var response = await _client.GetAsync("/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.RequestIdHeader),
            $"Response should contain {CorrelationIdMiddleware.RequestIdHeader} header");

        var requestId = response.Headers.GetValues(CorrelationIdMiddleware.RequestIdHeader).FirstOrDefault();
        Assert.NotNull(requestId);
        Assert.True(Guid.TryParse(requestId, out _), "Request ID should be a valid GUID");
    }

    [Fact]
    public async Task Response_UsesProvidedCorrelationId_WhenCorrelationIdHeaderSent()
    {
        // Arrange
        var expectedCorrelationId = Guid.NewGuid().ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add(CorrelationIdMiddleware.CorrelationIdHeader, expectedCorrelationId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.CorrelationIdHeader).FirstOrDefault();
        Assert.Equal(expectedCorrelationId, correlationId);
    }

    [Fact]
    public async Task Response_UsesProvidedRequestId_WhenRequestIdHeaderSent()
    {
        // Arrange
        var expectedRequestId = Guid.NewGuid().ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add(CorrelationIdMiddleware.RequestIdHeader, expectedRequestId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requestId = response.Headers.GetValues(CorrelationIdMiddleware.RequestIdHeader).FirstOrDefault();
        Assert.Equal(expectedRequestId, requestId);
    }

    [Fact]
    public async Task Response_PreferCorrelationIdHeader_OverRequestIdHeader()
    {
        // Arrange
        var expectedCorrelationId = Guid.NewGuid().ToString("D");
        var notExpectedRequestId = Guid.NewGuid().ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add(CorrelationIdMiddleware.CorrelationIdHeader, expectedCorrelationId);
        request.Headers.Add(CorrelationIdMiddleware.RequestIdHeader, notExpectedRequestId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.CorrelationIdHeader).FirstOrDefault();
        Assert.Equal(expectedCorrelationId, correlationId);
    }

    [Fact]
    public async Task Response_BothHeaders_HaveSameValue()
    {
        // Act
        var response = await _client.GetAsync("/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.CorrelationIdHeader).FirstOrDefault();
        var requestId = response.Headers.GetValues(CorrelationIdMiddleware.RequestIdHeader).FirstOrDefault();

        Assert.NotNull(correlationId);
        Assert.NotNull(requestId);
        Assert.Equal(correlationId, requestId);
    }

    [Fact]
    public async Task Response_CorrelationIdPersists_AcrossMultipleRequests()
    {
        // Arrange & Act
        var response1 = await _client.GetAsync("/healthz");
        var response2 = await _client.GetAsync("/healthz");

        // Assert
        var correlationId1 = response1.Headers.GetValues(CorrelationIdMiddleware.CorrelationIdHeader).FirstOrDefault();
        var correlationId2 = response2.Headers.GetValues(CorrelationIdMiddleware.CorrelationIdHeader).FirstOrDefault();

        Assert.NotNull(correlationId1);
        Assert.NotNull(correlationId2);
        Assert.NotEqual(correlationId1, correlationId2); // Each request should get a new correlation ID
    }

    [Fact]
    public async Task Response_ContainsCorrelationId_EvenForErrorResponses()
    {
        // Act - Hit a non-existent endpoint to get 404
        var response = await _client.GetAsync("/api/v1/nonexistent");

        // Assert
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.CorrelationIdHeader),
            $"Response should contain {CorrelationIdMiddleware.CorrelationIdHeader} header even for error responses");
    }

    [Fact]
    public async Task Response_AcceptsCustomCorrelationIdFormat()
    {
        // Arrange - Use a custom non-GUID format (some systems use different formats)
        var customCorrelationId = "my-service-request-12345";
        var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add(CorrelationIdMiddleware.CorrelationIdHeader, customCorrelationId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.CorrelationIdHeader).FirstOrDefault();
        Assert.Equal(customCorrelationId, correlationId);
    }
}

using System.Net;
using Xunit;

namespace Octopus.Api.Client.Tests;

public class AuthorizationDelegatingHandlerTests
{
    [Fact]
    public async Task SendAsync_WithToken_AttachesAuthorizationHeader()
    {
        // Arrange
        var expectedToken = "test-jwt-token";
        var tokenProvider = new StaticTokenProvider(expectedToken);

        var capturedRequest = (HttpRequestMessage?)null;
        var innerHandler = new TestMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new AuthorizationDelegatingHandler(tokenProvider, innerHandler);
        var httpClient = new HttpClient(handler);

        // Act
        await httpClient.GetAsync("https://localhost/api/test");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization.Scheme);
        Assert.Equal(expectedToken, capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_WithNullToken_DoesNotAttachAuthorizationHeader()
    {
        // Arrange
        var tokenProvider = new StaticTokenProvider(null);

        var capturedRequest = (HttpRequestMessage?)null;
        var innerHandler = new TestMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new AuthorizationDelegatingHandler(tokenProvider, innerHandler);
        var httpClient = new HttpClient(handler);

        // Act
        await httpClient.GetAsync("https://localhost/api/test");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_WithEmptyToken_DoesNotAttachAuthorizationHeader()
    {
        // Arrange
        var tokenProvider = new StaticTokenProvider(string.Empty);

        var capturedRequest = (HttpRequestMessage?)null;
        var innerHandler = new TestMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new AuthorizationDelegatingHandler(tokenProvider, innerHandler);
        var httpClient = new HttpClient(handler);

        // Act
        await httpClient.GetAsync("https://localhost/api/test");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_WithExistingAuthorizationHeader_DoesNotOverwrite()
    {
        // Arrange
        var tokenProvider = new StaticTokenProvider("new-token");

        var capturedRequest = (HttpRequestMessage?)null;
        var innerHandler = new TestMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new AuthorizationDelegatingHandler(tokenProvider, innerHandler);
        var httpClient = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "existing-token");

        // Act
        await httpClient.SendAsync(request);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("existing-token", capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_WithDelegateTokenProvider_CallsTokenFactory()
    {
        // Arrange
        var callCount = 0;
        var tokenProvider = new DelegateTokenProvider(_ =>
        {
            callCount++;
            return Task.FromResult<string?>("dynamic-token");
        });

        var capturedRequest = (HttpRequestMessage?)null;
        var innerHandler = new TestMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new AuthorizationDelegatingHandler(tokenProvider, innerHandler);
        var httpClient = new HttpClient(handler);

        // Act
        await httpClient.GetAsync("https://localhost/api/test");

        // Assert
        Assert.Equal(1, callCount);
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("dynamic-token", capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_TokenProviderCalledForEachRequest()
    {
        // Arrange
        var callCount = 0;
        var tokenProvider = new DelegateTokenProvider(_ =>
        {
            callCount++;
            return Task.FromResult<string?>($"token-{callCount}");
        });

        var capturedRequests = new List<HttpRequestMessage>();
        var innerHandler = new TestMessageHandler(request =>
        {
            capturedRequests.Add(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new AuthorizationDelegatingHandler(tokenProvider, innerHandler);
        var httpClient = new HttpClient(handler);

        // Act
        await httpClient.GetAsync("https://localhost/api/test1");
        await httpClient.GetAsync("https://localhost/api/test2");

        // Assert
        Assert.Equal(2, callCount);
        Assert.Equal(2, capturedRequests.Count);
        Assert.Equal("token-1", capturedRequests[0].Headers.Authorization?.Parameter);
        Assert.Equal("token-2", capturedRequests[1].Headers.Authorization?.Parameter);
    }

    [Fact]
    public void Constructor_WithNullTokenProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthorizationDelegatingHandler(null!));
    }

    /// <summary>
    /// Test message handler that captures requests and returns configurable responses.
    /// </summary>
    private class TestMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public TestMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}

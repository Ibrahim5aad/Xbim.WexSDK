using System.Net.Http.Headers;

namespace Octopus.Api.Client;

/// <summary>
/// A DelegatingHandler that automatically attaches Bearer tokens to HTTP requests.
/// Use this handler in the HttpClient pipeline to enable authenticated API calls.
/// </summary>
public class AuthorizationDelegatingHandler : DelegatingHandler
{
    private readonly IAuthTokenProvider _tokenProvider;

    /// <summary>
    /// Creates a new AuthorizationDelegatingHandler with the specified token provider.
    /// </summary>
    /// <param name="tokenProvider">The token provider to use for retrieving authentication tokens.</param>
    public AuthorizationDelegatingHandler(IAuthTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <summary>
    /// Creates a new AuthorizationDelegatingHandler with the specified token provider and inner handler.
    /// </summary>
    /// <param name="tokenProvider">The token provider to use for retrieving authentication tokens.</param>
    /// <param name="innerHandler">The inner handler which is responsible for processing the HTTP response messages.</param>
    public AuthorizationDelegatingHandler(IAuthTokenProvider tokenProvider, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Don't overwrite existing Authorization header
        if (request.Headers.Authorization == null)
        {
            var token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

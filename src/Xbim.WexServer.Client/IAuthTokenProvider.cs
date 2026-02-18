namespace Xbim.WexServer.Client;

/// <summary>
/// Provides authentication tokens for API requests.
/// Implement this interface to supply JWT or other bearer tokens to the Xbim API client.
/// </summary>
public interface IAuthTokenProvider
{
    /// <summary>
    /// Gets the current authentication token asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The bearer token string (without "Bearer " prefix), or null if no token is available.
    /// </returns>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A simple token provider that uses a delegate function to retrieve tokens.
/// Useful for scenarios where the token retrieval logic is simple or inline.
/// </summary>
public class DelegateTokenProvider : IAuthTokenProvider
{
    private readonly Func<CancellationToken, Task<string?>> _tokenFactory;

    /// <summary>
    /// Creates a new DelegateTokenProvider with the specified token factory function.
    /// </summary>
    /// <param name="tokenFactory">A function that returns the token asynchronously.</param>
    public DelegateTokenProvider(Func<CancellationToken, Task<string?>> tokenFactory)
    {
        _tokenFactory = tokenFactory ?? throw new ArgumentNullException(nameof(tokenFactory));
    }

    /// <summary>
    /// Creates a new DelegateTokenProvider with a synchronous token factory function.
    /// </summary>
    /// <param name="tokenFactory">A function that returns the token synchronously.</param>
    public DelegateTokenProvider(Func<string?> tokenFactory)
    {
        ArgumentNullException.ThrowIfNull(tokenFactory);
        _tokenFactory = _ => Task.FromResult(tokenFactory());
    }

    /// <inheritdoc />
    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return _tokenFactory(cancellationToken);
    }
}

/// <summary>
/// A token provider that always returns a static token.
/// Useful for testing or scenarios where the token doesn't change.
/// </summary>
public class StaticTokenProvider : IAuthTokenProvider
{
    private readonly string? _token;

    /// <summary>
    /// Creates a new StaticTokenProvider with the specified token.
    /// </summary>
    /// <param name="token">The static token to return, or null for no token.</param>
    public StaticTokenProvider(string? token)
    {
        _token = token;
    }

    /// <inheritdoc />
    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_token);
    }
}

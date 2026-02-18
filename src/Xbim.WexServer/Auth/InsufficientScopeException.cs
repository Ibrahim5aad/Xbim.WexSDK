namespace Xbim.WexServer.Auth;

/// <summary>
/// Exception thrown when a request lacks the required OAuth scope(s).
/// This maps to HTTP 403 Forbidden with an insufficient_scope error.
/// </summary>
public class InsufficientScopeException : Exception
{
    /// <summary>
    /// The scopes that were required for the operation.
    /// </summary>
    public IReadOnlyList<string> RequiredScopes { get; }

    /// <summary>
    /// The scopes that were present in the request.
    /// </summary>
    public IReadOnlyList<string> PresentScopes { get; }

    public InsufficientScopeException(string requiredScope)
        : this(new[] { requiredScope }, Array.Empty<string>())
    {
    }

    public InsufficientScopeException(IEnumerable<string> requiredScopes, IEnumerable<string> presentScopes)
        : base($"Insufficient scope. Required: {string.Join(", ", requiredScopes)}. Present: {(presentScopes.Any() ? string.Join(", ", presentScopes) : "none")}.")
    {
        RequiredScopes = requiredScopes.ToList().AsReadOnly();
        PresentScopes = presentScopes.ToList().AsReadOnly();
    }

    public InsufficientScopeException(string message, IEnumerable<string> requiredScopes, IEnumerable<string> presentScopes)
        : base(message)
    {
        RequiredScopes = requiredScopes.ToList().AsReadOnly();
        PresentScopes = presentScopes.ToList().AsReadOnly();
    }
}

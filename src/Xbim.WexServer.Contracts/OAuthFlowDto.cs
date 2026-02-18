namespace Xbim.WexServer.Contracts;

/// <summary>
/// OAuth 2.0 token response as defined in RFC 6749.
/// </summary>
public record OAuthTokenResponse
{
    /// <summary>
    /// The access token issued by the authorization server.
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// The type of the token (always "Bearer").
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// The lifetime in seconds of the access token.
    /// </summary>
    public int ExpiresIn { get; init; }

    /// <summary>
    /// The refresh token (if issued).
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Space-separated list of scopes granted.
    /// </summary>
    public string? Scope { get; init; }
}

/// <summary>
/// OAuth 2.0 error response as defined in RFC 6749.
/// </summary>
public record OAuthErrorResponse
{
    /// <summary>
    /// Error code (e.g., "invalid_request", "unauthorized_client", etc.)
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable error description.
    /// </summary>
    public string? ErrorDescription { get; init; }

    /// <summary>
    /// URI to a web page with error information.
    /// </summary>
    public string? ErrorUri { get; init; }
}

/// <summary>
/// Token request for authorization code grant.
/// </summary>
public record AuthorizationCodeTokenRequest
{
    /// <summary>
    /// Must be "authorization_code".
    /// </summary>
    public string GrantType { get; init; } = string.Empty;

    /// <summary>
    /// The authorization code received from the authorization endpoint.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// The redirect URI used in the authorization request (must match exactly).
    /// </summary>
    public string RedirectUri { get; init; } = string.Empty;

    /// <summary>
    /// The client identifier.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// The client secret (required for confidential clients).
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// PKCE code verifier (required for public clients, recommended for all).
    /// </summary>
    public string? CodeVerifier { get; init; }
}

/// <summary>
/// Standard OAuth 2.0 error codes as defined in RFC 6749.
/// </summary>
public static class OAuthErrorCodes
{
    /// <summary>
    /// The request is missing a required parameter or is otherwise malformed.
    /// </summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>
    /// The client is not authorized to request an authorization code.
    /// </summary>
    public const string UnauthorizedClient = "unauthorized_client";

    /// <summary>
    /// The resource owner or authorization server denied the request.
    /// </summary>
    public const string AccessDenied = "access_denied";

    /// <summary>
    /// The authorization server does not support the response type.
    /// </summary>
    public const string UnsupportedResponseType = "unsupported_response_type";

    /// <summary>
    /// The requested scope is invalid, unknown, or malformed.
    /// </summary>
    public const string InvalidScope = "invalid_scope";

    /// <summary>
    /// The authorization server encountered an unexpected condition.
    /// </summary>
    public const string ServerError = "server_error";

    /// <summary>
    /// The authorization server is currently unavailable.
    /// </summary>
    public const string TemporarilyUnavailable = "temporarily_unavailable";

    /// <summary>
    /// The authorization grant or refresh token is invalid, expired, or revoked.
    /// </summary>
    public const string InvalidGrant = "invalid_grant";

    /// <summary>
    /// Client authentication failed.
    /// </summary>
    public const string InvalidClient = "invalid_client";

    /// <summary>
    /// The authorization grant type is not supported by the authorization server.
    /// </summary>
    public const string UnsupportedGrantType = "unsupported_grant_type";
}

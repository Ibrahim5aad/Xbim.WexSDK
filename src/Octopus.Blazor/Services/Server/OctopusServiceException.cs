using Octopus.Api.Client;

namespace Octopus.Blazor.Services.Server;

/// <summary>
/// Exception thrown by server-backed services when API calls fail.
/// <para>
/// Provides structured access to HTTP status codes and response data for predictable error handling.
/// </para>
/// </summary>
public class OctopusServiceException : Exception
{
    /// <summary>
    /// Gets the HTTP status code from the API response.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the raw response body, if available.
    /// </summary>
    public string? Response { get; }

    /// <summary>
    /// Gets whether this is an authentication error (401 Unauthorized).
    /// </summary>
    public bool IsUnauthorized => StatusCode == 401;

    /// <summary>
    /// Gets whether this is an authorization error (403 Forbidden).
    /// </summary>
    public bool IsForbidden => StatusCode == 403;

    /// <summary>
    /// Gets whether this is a not found error (404 Not Found).
    /// </summary>
    public bool IsNotFound => StatusCode == 404;

    /// <summary>
    /// Gets whether this is a conflict error (409 Conflict).
    /// </summary>
    public bool IsConflict => StatusCode == 409;

    /// <summary>
    /// Gets whether this is a validation error (400 Bad Request).
    /// </summary>
    public bool IsBadRequest => StatusCode == 400;

    /// <summary>
    /// Gets whether this is a server error (5xx).
    /// </summary>
    public bool IsServerError => StatusCode >= 500 && StatusCode < 600;

    /// <summary>
    /// Creates a new OctopusServiceException.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="response">The raw response body.</param>
    /// <param name="innerException">The inner exception.</param>
    public OctopusServiceException(string message, int statusCode, string? response = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Response = response;
    }

    /// <summary>
    /// Creates an OctopusServiceException from an OctopusApiException.
    /// </summary>
    /// <param name="ex">The API exception to wrap.</param>
    /// <returns>A new OctopusServiceException.</returns>
    public static OctopusServiceException FromApiException(OctopusApiException ex)
    {
        var message = ex.StatusCode switch
        {
            401 => "Authentication required. Please ensure you are logged in.",
            403 => "Access denied. You do not have permission to perform this action.",
            404 => "The requested resource was not found.",
            409 => "A conflict occurred. The resource may have been modified.",
            400 => "Invalid request. Please check your input.",
            >= 500 and < 600 => "A server error occurred. Please try again later.",
            _ => ex.Message
        };

        return new OctopusServiceException(message, ex.StatusCode, ex.Response, ex);
    }
}

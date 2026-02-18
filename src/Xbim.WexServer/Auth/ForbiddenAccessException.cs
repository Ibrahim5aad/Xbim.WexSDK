namespace Xbim.WexServer.Auth;

/// <summary>
/// Exception thrown when a user attempts to access a resource they are not authorized to access.
/// This maps to HTTP 403 Forbidden.
/// </summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException()
        : base("Access to the requested resource is forbidden.")
    {
    }

    public ForbiddenAccessException(string message)
        : base(message)
    {
    }

    public ForbiddenAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

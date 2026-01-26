namespace Octopus.Server.Abstractions.Auth;

/// <summary>
/// Represents the current user context for a request.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Gets whether a user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the user's unique identifier (local User.Id).
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Gets the user's subject claim from the identity provider.
    /// </summary>
    string? Subject { get; }

    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Gets the user's display name.
    /// </summary>
    string? DisplayName { get; }
}

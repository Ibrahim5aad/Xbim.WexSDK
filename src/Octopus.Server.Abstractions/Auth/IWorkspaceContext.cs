namespace Octopus.Server.Abstractions.Auth;

/// <summary>
/// Represents the current workspace context for a request.
/// The workspace context is derived from the OAuth token's "tid" (tenant ID) claim.
/// This enforces multi-tenant isolation by binding requests to a specific workspace.
/// </summary>
public interface IWorkspaceContext
{
    /// <summary>
    /// Gets whether a workspace context is bound for the current request.
    /// Returns true if the request has a valid "tid" claim in the token.
    /// </summary>
    bool IsBound { get; }

    /// <summary>
    /// Gets the workspace ID from the token's "tid" claim.
    /// Returns null if no workspace context is bound (e.g., dev auth mode).
    /// </summary>
    Guid? WorkspaceId { get; }

    /// <summary>
    /// Gets the workspace ID or throws if no workspace context is bound.
    /// Use this when workspace isolation is required.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no workspace context is bound.</exception>
    Guid RequiredWorkspaceId { get; }
}

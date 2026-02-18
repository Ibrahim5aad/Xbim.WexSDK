namespace Xbim.WexServer.App.Auth;

/// <summary>
/// Exception thrown when a request attempts to access a resource in a different workspace
/// than the one bound to the token. This maps to HTTP 403 Forbidden.
/// </summary>
public class WorkspaceIsolationException : Exception
{
    /// <summary>
    /// The workspace ID bound to the current token.
    /// </summary>
    public Guid TokenWorkspaceId { get; }

    /// <summary>
    /// The workspace ID of the resource being accessed.
    /// </summary>
    public Guid ResourceWorkspaceId { get; }

    public WorkspaceIsolationException(Guid tokenWorkspaceId, Guid resourceWorkspaceId)
        : base($"Cross-workspace access denied. Token is bound to workspace {tokenWorkspaceId}, but resource belongs to workspace {resourceWorkspaceId}.")
    {
        TokenWorkspaceId = tokenWorkspaceId;
        ResourceWorkspaceId = resourceWorkspaceId;
    }

    public WorkspaceIsolationException(string message, Guid tokenWorkspaceId, Guid resourceWorkspaceId)
        : base(message)
    {
        TokenWorkspaceId = tokenWorkspaceId;
        ResourceWorkspaceId = resourceWorkspaceId;
    }
}

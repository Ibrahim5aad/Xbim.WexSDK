using System.Security.Claims;
using Octopus.Server.Abstractions.Auth;

namespace Octopus.Server.App.Auth;

/// <summary>
/// Implementation of IWorkspaceContext that reads the workspace context from HttpContext.
/// The workspace ID is extracted from the OAuth token's "tid" (tenant ID) claim.
/// </summary>
public class HttpContextWorkspaceContext : IWorkspaceContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private bool _parsed;
    private Guid? _workspaceId;

    public HttpContextWorkspaceContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsBound => WorkspaceId.HasValue;

    /// <inheritdoc />
    public Guid? WorkspaceId
    {
        get
        {
            if (_parsed)
            {
                return _workspaceId;
            }

            _parsed = true;

            // The "tid" claim contains the workspace/tenant ID from OAuth tokens
            var tidClaim = User?.FindFirst("tid")?.Value;
            if (!string.IsNullOrEmpty(tidClaim) && Guid.TryParse(tidClaim, out var workspaceId))
            {
                _workspaceId = workspaceId;
            }

            return _workspaceId;
        }
    }

    /// <inheritdoc />
    public Guid RequiredWorkspaceId
    {
        get
        {
            if (!WorkspaceId.HasValue)
            {
                throw new InvalidOperationException(
                    "No workspace context is bound. The request token must include a 'tid' claim. " +
                    "This typically occurs in development mode or when using OIDC tokens without workspace scoping.");
            }
            return WorkspaceId.Value;
        }
    }
}

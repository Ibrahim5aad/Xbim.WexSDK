using System.Security.Claims;
using Xbim.WexServer.Abstractions.Auth;

namespace Xbim.WexServer.Auth;

/// <summary>
/// Implementation of IUserContext that reads user information from HttpContext.
/// The UserId is populated by UserProvisioningMiddleware after the user is provisioned.
/// </summary>
public class HttpContextUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            // UserId is stored in HttpContext.Items by UserProvisioningMiddleware
            if (_httpContextAccessor.HttpContext?.Items.TryGetValue("XbimUserId", out var value) == true
                && value is Guid userId)
            {
                return userId;
            }
            return null;
        }
    }

    public string? Subject => User?.FindFirstValue("sub") ?? User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Email => User?.FindFirstValue("email") ?? User?.FindFirstValue(ClaimTypes.Email);

    public string? DisplayName => User?.FindFirstValue("name") ?? User?.FindFirstValue(ClaimTypes.Name);
}

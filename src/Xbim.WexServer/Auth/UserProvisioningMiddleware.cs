using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.Auth;

/// <summary>
/// Middleware that auto-provisions a local User entity based on the authenticated principal.
/// This ensures that every authenticated request has a corresponding User record in the database.
/// </summary>
public class UserProvisioningMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserProvisioningMiddleware> _logger;

    public UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, XbimDbContext dbContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var subject = context.User.FindFirstValue("sub")
                          ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(subject))
            {
                var user = await ProvisionUserAsync(context, dbContext, subject);
                if (user != null)
                {
                    // Store the user ID in HttpContext.Items for IUserContext to access
                    context.Items["XbimUserId"] = user.Id;
                }
            }
        }

        await _next(context);
    }

    private async Task<User?> ProvisionUserAsync(HttpContext context, XbimDbContext dbContext, string subject)
    {
        // Try to find existing user by subject
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Subject == subject);

        if (user != null)
        {
            // Update last login time
            user.LastLoginAt = DateTimeOffset.UtcNow;

            // Update email and display name if they changed
            var email = context.User.FindFirstValue("email")
                        ?? context.User.FindFirstValue(ClaimTypes.Email);
            var displayName = context.User.FindFirstValue("name")
                              ?? context.User.FindFirstValue(ClaimTypes.Name);

            if (!string.IsNullOrEmpty(email) && user.Email != email)
            {
                user.Email = email;
            }
            if (!string.IsNullOrEmpty(displayName) && user.DisplayName != displayName)
            {
                user.DisplayName = displayName;
            }

            await dbContext.SaveChangesAsync();
            _logger.LogDebug("Updated existing user {UserId} with subject {Subject}", user.Id, subject);
            return user;
        }

        // Create new user
        var email2 = context.User.FindFirstValue("email")
                    ?? context.User.FindFirstValue(ClaimTypes.Email);
        var displayName2 = context.User.FindFirstValue("name")
                          ?? context.User.FindFirstValue(ClaimTypes.Name);

        user = new User
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            Email = email2,
            DisplayName = displayName2,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Provisioned new user {UserId} with subject {Subject}", user.Id, subject);
        return user;
    }
}

/// <summary>
/// Extension methods for adding user provisioning middleware.
/// </summary>
public static class UserProvisioningMiddlewareExtensions
{
    public static IApplicationBuilder UseUserProvisioning(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UserProvisioningMiddleware>();
    }
}

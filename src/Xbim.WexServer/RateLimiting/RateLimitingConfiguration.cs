using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Xbim.WexServer.RateLimiting;

/// <summary>
/// Configuration options for rate limiting.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Whether rate limiting is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Options for the upload reserve endpoint rate limit.
    /// </summary>
    public FixedWindowOptions UploadReserve { get; set; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 60
    };

    /// <summary>
    /// Options for the upload content endpoint rate limit.
    /// </summary>
    public FixedWindowOptions UploadContent { get; set; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60
    };

    /// <summary>
    /// Options for the upload commit endpoint rate limit.
    /// </summary>
    public FixedWindowOptions UploadCommit { get; set; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60
    };
}

/// <summary>
/// Fixed window rate limiter options.
/// </summary>
public class FixedWindowOptions
{
    /// <summary>
    /// Maximum number of requests permitted in the time window.
    /// </summary>
    public int PermitLimit { get; set; }

    /// <summary>
    /// Duration of the time window in seconds.
    /// </summary>
    public int WindowSeconds { get; set; }

    /// <summary>
    /// Maximum number of requests that can be queued. Default is 0 (no queuing).
    /// </summary>
    public int QueueLimit { get; set; } = 0;
}

/// <summary>
/// Rate limiting policy names.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Policy for upload reserve endpoint.
    /// </summary>
    public const string UploadReserve = "upload-reserve";

    /// <summary>
    /// Policy for upload content endpoint.
    /// </summary>
    public const string UploadContent = "upload-content";

    /// <summary>
    /// Policy for upload commit endpoint.
    /// </summary>
    public const string UploadCommit = "upload-commit";
}

/// <summary>
/// Extension methods for rate limiting configuration.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Adds rate limiting services with upload-specific policies.
    /// </summary>
    public static IServiceCollection AddUploadRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.SectionName).Bind(options);

        if (!options.Enabled)
        {
            // Register a no-op rate limiter when disabled
            services.AddRateLimiter(_ => { });
            return services;
        }

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Configure the response for rate-limited requests
            limiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
                    ? retryAfterValue.TotalSeconds
                    : options.UploadContent.WindowSeconds;

                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter).ToString();

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Too Many Requests",
                    message = "Rate limit exceeded. Please try again later.",
                    retryAfterSeconds = (int)retryAfter
                }, cancellationToken);
            };

            // Upload reserve policy - more permissive (creating sessions is cheap)
            limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadReserve, limiter =>
            {
                limiter.PermitLimit = options.UploadReserve.PermitLimit;
                limiter.Window = TimeSpan.FromSeconds(options.UploadReserve.WindowSeconds);
                limiter.QueueLimit = options.UploadReserve.QueueLimit;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Upload content policy - more restrictive (actual data transfer)
            limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadContent, limiter =>
            {
                limiter.PermitLimit = options.UploadContent.PermitLimit;
                limiter.Window = TimeSpan.FromSeconds(options.UploadContent.WindowSeconds);
                limiter.QueueLimit = options.UploadContent.QueueLimit;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Upload commit policy - moderate (finalizing uploads)
            limiterOptions.AddFixedWindowLimiter(RateLimitPolicies.UploadCommit, limiter =>
            {
                limiter.PermitLimit = options.UploadCommit.PermitLimit;
                limiter.Window = TimeSpan.FromSeconds(options.UploadCommit.WindowSeconds);
                limiter.QueueLimit = options.UploadCommit.QueueLimit;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        return services;
    }
}

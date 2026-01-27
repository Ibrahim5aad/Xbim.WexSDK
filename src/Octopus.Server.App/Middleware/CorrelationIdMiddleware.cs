using System.Diagnostics;

namespace Octopus.Server.App.Middleware;

/// <summary>
/// Middleware that ensures each request has a correlation ID for tracing.
/// The correlation ID is read from incoming headers or generated if not present,
/// added to response headers, and included in the logging scope.
/// </summary>
public class CorrelationIdMiddleware
{
    /// <summary>
    /// The key used to store the correlation ID in HttpContext.Items.
    /// </summary>
    public const string CorrelationIdItemKey = "CorrelationId";

    /// <summary>
    /// The header name for correlation ID in requests and responses.
    /// </summary>
    public const string CorrelationIdHeader = "X-Correlation-ID";

    /// <summary>
    /// Alternative header name for request ID (commonly used).
    /// </summary>
    public const string RequestIdHeader = "X-Request-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
    /// </summary>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request to ensure correlation ID is present and logged.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Try to get correlation ID from various sources in priority order
        var correlationId = GetCorrelationIdFromRequest(context);

        // Store in HttpContext.Items for downstream access
        context.Items[CorrelationIdItemKey] = correlationId;

        // Add correlation ID to response headers early (before any potential response writes)
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
            {
                context.Response.Headers[CorrelationIdHeader] = correlationId;
            }
            if (!context.Response.Headers.ContainsKey(RequestIdHeader))
            {
                context.Response.Headers[RequestIdHeader] = correlationId;
            }
            return Task.CompletedTask;
        });

        // Create a logging scope that includes the correlation ID for all log entries in this request
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
            ["RequestMethod"] = context.Request.Method
        }))
        {
            _logger.LogDebug("Request started with CorrelationId: {CorrelationId}", correlationId);

            await _next(context);

            _logger.LogDebug("Request completed with CorrelationId: {CorrelationId}, StatusCode: {StatusCode}",
                correlationId, context.Response.StatusCode);
        }
    }

    private static string GetCorrelationIdFromRequest(HttpContext context)
    {
        // Priority 1: X-Correlation-ID header
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationHeader)
            && !string.IsNullOrWhiteSpace(correlationHeader))
        {
            return correlationHeader.ToString();
        }

        // Priority 2: X-Request-ID header
        if (context.Request.Headers.TryGetValue(RequestIdHeader, out var requestIdHeader)
            && !string.IsNullOrWhiteSpace(requestIdHeader))
        {
            return requestIdHeader.ToString();
        }

        // Priority 3: W3C Trace Context (traceparent header parsed by ASP.NET Core)
        var activity = Activity.Current;
        if (activity != null && !string.IsNullOrEmpty(activity.TraceId.ToString())
            && activity.TraceId != default)
        {
            return activity.TraceId.ToString();
        }

        // Priority 4: Generate a new correlation ID
        return Guid.NewGuid().ToString("D");
    }
}

/// <summary>
/// Extension methods for registering the correlation ID middleware.
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>
    /// Adds the correlation ID middleware to the application pipeline.
    /// This middleware should be added early in the pipeline, before authentication.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    /// <summary>
    /// Gets the correlation ID from the current HTTP context.
    /// Returns null if no correlation ID is available.
    /// </summary>
    public static string? GetCorrelationId(this HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdMiddleware.CorrelationIdItemKey, out var value)
            && value is string correlationId)
        {
            return correlationId;
        }

        return null;
    }
}

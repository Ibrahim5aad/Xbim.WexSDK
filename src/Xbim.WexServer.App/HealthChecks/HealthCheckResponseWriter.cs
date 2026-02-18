using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Xbim.WexServer.App.HealthChecks;

/// <summary>
/// Custom response writer for health check endpoints that outputs detailed JSON.
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Writes a detailed JSON response for health check results.
    /// </summary>
    public static async Task WriteDetailedResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new HealthCheckResponse
        {
            Status = report.Status.ToString().ToLowerInvariant(),
            TotalDuration = report.TotalDuration.TotalMilliseconds,
            Checks = report.Entries.Select(entry => new HealthCheckEntry
            {
                Name = entry.Key,
                Status = entry.Value.Status.ToString().ToLowerInvariant(),
                Duration = entry.Value.Duration.TotalMilliseconds,
                Description = entry.Value.Description,
                Exception = entry.Value.Exception?.Message,
                Data = entry.Value.Data?.Count > 0
                    ? entry.Value.Data.ToDictionary(
                        d => d.Key,
                        d => d.Value)
                    : null
            }).ToList()
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private class HealthCheckResponse
    {
        public required string Status { get; init; }
        public double TotalDuration { get; init; }
        public List<HealthCheckEntry> Checks { get; init; } = [];
    }

    private class HealthCheckEntry
    {
        public required string Name { get; init; }
        public required string Status { get; init; }
        public double Duration { get; init; }
        public string? Description { get; init; }
        public string? Exception { get; init; }
        public Dictionary<string, object>? Data { get; init; }
    }
}

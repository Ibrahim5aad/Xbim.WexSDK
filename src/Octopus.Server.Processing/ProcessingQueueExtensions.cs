using System.Text.Json;
using Octopus.Server.Abstractions.Processing;

namespace Octopus.Server.Processing;

/// <summary>
/// Extension methods for enqueueing typed jobs.
/// </summary>
public static class ProcessingQueueExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Enqueues a typed job with automatic JSON serialization.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="queue">The processing queue.</param>
    /// <param name="jobType">The job type string (must match a registered handler).</param>
    /// <param name="payload">The job payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated job ID.</returns>
    public static async ValueTask<string> EnqueueAsync<TPayload>(
        this IProcessingQueue queue,
        string jobType,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrEmpty(jobType);

        var jobId = Guid.NewGuid().ToString("N");
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        var envelope = new JobEnvelope
        {
            JobId = jobId,
            Type = jobType,
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await queue.EnqueueAsync(envelope, cancellationToken);
        return jobId;
    }

    /// <summary>
    /// Enqueues a typed job with a specified job ID.
    /// Use this when you want to control the job ID for idempotency.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="queue">The processing queue.</param>
    /// <param name="jobId">The job ID (for idempotency).</param>
    /// <param name="jobType">The job type string (must match a registered handler).</param>
    /// <param name="payload">The job payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask EnqueueAsync<TPayload>(
        this IProcessingQueue queue,
        string jobId,
        string jobType,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        ArgumentException.ThrowIfNullOrEmpty(jobType);
        ArgumentNullException.ThrowIfNull(payload);

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        var envelope = new JobEnvelope
        {
            JobId = jobId,
            Type = jobType,
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await queue.EnqueueAsync(envelope, cancellationToken);
    }
}

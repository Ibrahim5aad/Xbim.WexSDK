namespace Xbim.WexServer.Abstractions.Processing;

/// <summary>
/// Envelope that wraps a job payload for queue transport.
/// </summary>
public record JobEnvelope
{
    /// <summary>
    /// Unique identifier for the job, used for idempotency.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// The type of job (used to dispatch to the correct handler).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// JSON-serialized payload for the job.
    /// </summary>
    public required string PayloadJson { get; init; }

    /// <summary>
    /// When the job was created/enqueued.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Schema version for the payload (for future compatibility).
    /// </summary>
    public int Version { get; init; } = 1;
}

/// <summary>
/// Abstraction for the processing queue.
/// </summary>
public interface IProcessingQueue
{
    /// <summary>
    /// Enqueues a job envelope for processing.
    /// </summary>
    /// <param name="envelope">The job envelope to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask EnqueueAsync(JobEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues the next job envelope for processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next job envelope, or null if queue is empty and closed.</returns>
    ValueTask<JobEnvelope?> DequeueAsync(CancellationToken cancellationToken = default);
}

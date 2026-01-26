using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Abstractions.Processing;

/// <summary>
/// Represents a job to be processed.
/// </summary>
public record ProcessingJobRequest
{
    /// <summary>
    /// The ID of the processing job entity.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// The model version ID this job is for.
    /// </summary>
    public Guid ModelVersionId { get; init; }

    /// <summary>
    /// The type of processing job.
    /// </summary>
    public ProcessingJobType JobType { get; init; }
}

/// <summary>
/// Abstraction for the processing queue.
/// </summary>
public interface IProcessingQueue
{
    /// <summary>
    /// Enqueues a job for processing.
    /// </summary>
    /// <param name="job">The job to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask EnqueueAsync(ProcessingJobRequest job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues the next job for processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next job, or null if queue is empty and closed.</returns>
    ValueTask<ProcessingJobRequest?> DequeueAsync(CancellationToken cancellationToken = default);
}

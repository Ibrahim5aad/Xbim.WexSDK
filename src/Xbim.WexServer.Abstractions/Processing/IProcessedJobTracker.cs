namespace Xbim.WexServer.Abstractions.Processing;

/// <summary>
/// Tracks processed jobs for idempotency.
/// Ensures duplicate job deliveries do not re-run side effects.
/// </summary>
public interface IProcessedJobTracker
{
    /// <summary>
    /// Attempts to mark a job as being processed.
    /// Returns false if the job has already been processed (duplicate delivery).
    /// </summary>
    /// <param name="jobId">The unique job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this is the first time processing this job; false if already processed.</returns>
    Task<bool> TryMarkAsProcessingAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as successfully completed.
    /// </summary>
    /// <param name="jobId">The unique job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsCompletedAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as failed, allowing it to be retried.
    /// </summary>
    /// <param name="jobId">The unique job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsFailedAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a job has already been successfully processed.
    /// </summary>
    /// <param name="jobId">The unique job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the job was successfully completed; false otherwise.</returns>
    Task<bool> IsCompletedAsync(string jobId, CancellationToken cancellationToken = default);
}

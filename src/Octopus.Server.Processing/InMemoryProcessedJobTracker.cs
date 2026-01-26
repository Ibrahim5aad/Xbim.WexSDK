using System.Collections.Concurrent;
using Octopus.Server.Abstractions.Processing;

namespace Octopus.Server.Processing;

/// <summary>
/// In-memory implementation of processed job tracking for idempotency.
/// Suitable for single-instance deployments and testing.
/// For distributed deployments, use a database-backed implementation.
/// </summary>
public sealed class InMemoryProcessedJobTracker : IProcessedJobTracker
{
    private enum JobState
    {
        Processing,
        Completed,
        Failed
    }

    private readonly ConcurrentDictionary<string, JobState> _processedJobs = new();

    /// <inheritdoc />
    public Task<bool> TryMarkAsProcessingAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);

        // Try to add the job as processing. If it already exists, check if it's completed.
        // If completed, return false (duplicate). If failed, allow retry.
        var success = _processedJobs.TryAdd(jobId, JobState.Processing);

        if (!success)
        {
            // Job already exists - check its state
            if (_processedJobs.TryGetValue(jobId, out var existingState))
            {
                if (existingState == JobState.Completed)
                {
                    // Already completed - this is a duplicate
                    return Task.FromResult(false);
                }

                if (existingState == JobState.Failed)
                {
                    // Failed - allow retry by updating to Processing
                    _processedJobs.TryUpdate(jobId, JobState.Processing, JobState.Failed);
                    return Task.FromResult(true);
                }

                // Still processing - treat as duplicate
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(success);
    }

    /// <inheritdoc />
    public Task MarkAsCompletedAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        _processedJobs.AddOrUpdate(jobId, JobState.Completed, (_, _) => JobState.Completed);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkAsFailedAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        _processedJobs.AddOrUpdate(jobId, JobState.Failed, (_, _) => JobState.Failed);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsCompletedAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);

        if (_processedJobs.TryGetValue(jobId, out var state))
        {
            return Task.FromResult(state == JobState.Completed);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets the count of tracked jobs (for testing/monitoring).
    /// </summary>
    public int TrackedCount => _processedJobs.Count;

    /// <summary>
    /// Clears all tracked jobs (for testing).
    /// </summary>
    public void Clear() => _processedJobs.Clear();
}

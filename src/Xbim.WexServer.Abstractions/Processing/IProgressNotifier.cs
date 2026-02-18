namespace Xbim.WexServer.Abstractions.Processing;

/// <summary>
/// Progress update notification for a processing job.
/// </summary>
public record ProcessingProgress
{
    /// <summary>
    /// The job ID this progress is for.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// The model version ID being processed.
    /// </summary>
    public required Guid ModelVersionId { get; init; }

    /// <summary>
    /// Current processing stage.
    /// </summary>
    public required string Stage { get; init; }

    /// <summary>
    /// Percentage complete (0-100).
    /// </summary>
    public int PercentComplete { get; init; }

    /// <summary>
    /// Human-readable message describing current activity.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Whether processing has completed (successfully or with failure).
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Whether processing completed successfully.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp of this progress update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Interface for dispatching processing progress notifications.
/// Implementations can hook into SignalR, webhooks, etc.
/// </summary>
public interface IProgressNotifier
{
    /// <summary>
    /// Sends a progress notification for a processing job.
    /// </summary>
    /// <param name="progress">The progress update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task NotifyProgressAsync(ProcessingProgress progress, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default no-op implementation of IProgressNotifier.
/// Used when no notification system is configured.
/// </summary>
public class NullProgressNotifier : IProgressNotifier
{
    public static readonly NullProgressNotifier Instance = new();

    public Task NotifyProgressAsync(ProcessingProgress progress, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

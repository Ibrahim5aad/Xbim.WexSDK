using Microsoft.Extensions.Logging;
using Xbim.WexServer.Abstractions.Processing;

namespace Xbim.WexServer.App.Processing;

/// <summary>
/// Progress notifier that logs notifications.
/// Useful for debugging and testing.
/// </summary>
public class LoggingProgressNotifier : IProgressNotifier
{
    private readonly ILogger<LoggingProgressNotifier> _logger;

    public LoggingProgressNotifier(ILogger<LoggingProgressNotifier> logger)
    {
        _logger = logger;
    }

    public Task NotifyProgressAsync(ProcessingProgress progress, CancellationToken cancellationToken = default)
    {
        if (progress.IsComplete)
        {
            if (progress.IsSuccess)
            {
                _logger.LogInformation(
                    "Job {JobId} completed successfully for ModelVersion {ModelVersionId}",
                    progress.JobId, progress.ModelVersionId);
            }
            else
            {
                _logger.LogWarning(
                    "Job {JobId} failed for ModelVersion {ModelVersionId}: {Error}",
                    progress.JobId, progress.ModelVersionId, progress.ErrorMessage);
            }
        }
        else
        {
            _logger.LogDebug(
                "Job {JobId} progress: {Stage} ({Percent}%) - {Message}",
                progress.JobId, progress.Stage, progress.PercentComplete, progress.Message);
        }

        return Task.CompletedTask;
    }
}

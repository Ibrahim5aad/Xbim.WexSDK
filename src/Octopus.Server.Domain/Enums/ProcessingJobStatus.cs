namespace Octopus.Server.Domain.Enums;

/// <summary>
/// Processing job status.
/// </summary>
public enum ProcessingJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

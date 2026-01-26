using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents a processing job in the queue.
/// </summary>
public class ProcessingJob
{
    public Guid Id { get; set; }
    public Guid ModelVersionId { get; set; }
    public ProcessingJobType JobType { get; set; }
    public ProcessingJobStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation properties
    public ModelVersion? ModelVersion { get; set; }
}

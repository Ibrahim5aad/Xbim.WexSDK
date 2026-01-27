using Octopus.Server.Domain.Enums;

namespace Octopus.Server.Domain.Entities;

/// <summary>
/// Represents an upload session for chunked/resumable uploads.
/// </summary>
public class UploadSession
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long? ExpectedSizeBytes { get; set; }
    public UploadSessionStatus Status { get; set; }
    public UploadMode UploadMode { get; set; }
    public string? TempStorageKey { get; set; }
    public string? DirectUploadUrl { get; set; }
    public Guid? CommittedFileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    // Navigation properties
    public Project? Project { get; set; }
    public FileEntity? CommittedFile { get; set; }
}

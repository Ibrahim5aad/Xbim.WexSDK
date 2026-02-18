using Xbim.WexServer.Domain.Enums;

namespace Xbim.WexServer.Domain.Entities;

/// <summary>
/// Represents a relationship between files.
/// </summary>
public class FileLink
{
    public Guid Id { get; set; }
    public Guid SourceFileId { get; set; }
    public Guid TargetFileId { get; set; }
    public FileLinkType LinkType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public FileEntity? SourceFile { get; set; }
    public FileEntity? TargetFile { get; set; }
}

namespace Xbim.WexServer.Domain.Enums;

/// <summary>
/// Upload session status.
/// </summary>
public enum UploadSessionStatus
{
    Reserved = 0,
    Uploading = 1,
    Committed = 2,
    Failed = 3,
    Expired = 4
}

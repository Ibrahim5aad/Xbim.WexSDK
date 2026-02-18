namespace Xbim.WexServer.Domain.Enums;

/// <summary>
/// Specifies how file content is uploaded to storage.
/// </summary>
public enum UploadMode
{
    /// <summary>
    /// Content is uploaded through the server (server-proxy mode).
    /// </summary>
    ServerProxy = 0,

    /// <summary>
    /// Content is uploaded directly to storage using a SAS URL (direct-to-blob mode).
    /// </summary>
    DirectToBlob = 1
}

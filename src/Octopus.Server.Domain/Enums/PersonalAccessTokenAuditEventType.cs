namespace Octopus.Server.Domain.Enums;

/// <summary>
/// Types of audit events for Personal Access Tokens.
/// </summary>
public enum PersonalAccessTokenAuditEventType
{
    /// <summary>
    /// PAT was created.
    /// </summary>
    Created = 0,

    /// <summary>
    /// PAT was used to authenticate an API request.
    /// </summary>
    Used = 1,

    /// <summary>
    /// PAT was revoked by the owner.
    /// </summary>
    RevokedByUser = 2,

    /// <summary>
    /// PAT was revoked by a workspace admin.
    /// </summary>
    RevokedByAdmin = 3,

    /// <summary>
    /// PAT expired naturally.
    /// </summary>
    Expired = 4,

    /// <summary>
    /// PAT name or description was updated.
    /// </summary>
    Updated = 5
}

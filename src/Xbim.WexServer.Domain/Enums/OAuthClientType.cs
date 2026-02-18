namespace Xbim.WexServer.Domain.Enums;

/// <summary>
/// OAuth 2.0 client type as defined in RFC 6749.
/// </summary>
public enum OAuthClientType
{
    /// <summary>
    /// Public clients cannot securely store credentials (SPAs, mobile apps, desktop apps).
    /// Must use PKCE for authorization code flow.
    /// </summary>
    Public = 0,

    /// <summary>
    /// Confidential clients can securely store credentials (server-side apps).
    /// Can use client secret for authentication.
    /// </summary>
    Confidential = 1
}

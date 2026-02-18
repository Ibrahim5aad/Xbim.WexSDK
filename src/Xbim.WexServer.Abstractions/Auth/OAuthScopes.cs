namespace Xbim.WexServer.Abstractions.Auth;

/// <summary>
/// Standard OAuth scopes for the Xbim API.
/// These scopes define the permissions that can be granted to OAuth apps and PATs.
/// </summary>
public static class OAuthScopes
{
    // Workspace scopes
    public const string WorkspacesRead = "workspaces:read";
    public const string WorkspacesWrite = "workspaces:write";

    // Project scopes
    public const string ProjectsRead = "projects:read";
    public const string ProjectsWrite = "projects:write";

    // File scopes
    public const string FilesRead = "files:read";
    public const string FilesWrite = "files:write";

    // Model scopes
    public const string ModelsRead = "models:read";
    public const string ModelsWrite = "models:write";

    // Processing scopes
    public const string ProcessingRead = "processing:read";
    public const string ProcessingWrite = "processing:write";

    // OAuth app management scopes
    public const string OAuthAppsRead = "oauth_apps:read";
    public const string OAuthAppsWrite = "oauth_apps:write";
    public const string OAuthAppsAdmin = "oauth_apps:admin";

    // PAT management scopes
    public const string PatsRead = "pats:read";
    public const string PatsWrite = "pats:write";
    public const string PatsAdmin = "pats:admin";

    /// <summary>
    /// All defined scopes for validation purposes.
    /// </summary>
    public static readonly IReadOnlySet<string> AllScopes = new HashSet<string>
    {
        WorkspacesRead, WorkspacesWrite,
        ProjectsRead, ProjectsWrite,
        FilesRead, FilesWrite,
        ModelsRead, ModelsWrite,
        ProcessingRead, ProcessingWrite,
        OAuthAppsRead, OAuthAppsWrite, OAuthAppsAdmin,
        PatsRead, PatsWrite, PatsAdmin
    };

    /// <summary>
    /// Validates that all provided scopes are valid OAuth scopes.
    /// </summary>
    /// <param name="scopes">The scopes to validate.</param>
    /// <returns>True if all scopes are valid.</returns>
    public static bool AreValidScopes(IEnumerable<string> scopes)
    {
        return scopes.All(s => AllScopes.Contains(s));
    }

    /// <summary>
    /// Gets the invalid scopes from a list of scopes.
    /// </summary>
    /// <param name="scopes">The scopes to check.</param>
    /// <returns>A list of invalid scopes.</returns>
    public static IReadOnlyList<string> GetInvalidScopes(IEnumerable<string> scopes)
    {
        return scopes.Where(s => !AllScopes.Contains(s)).ToList();
    }
}

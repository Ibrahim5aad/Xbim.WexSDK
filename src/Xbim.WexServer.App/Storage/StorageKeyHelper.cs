using System.Security.Cryptography;

namespace Xbim.WexServer.App.Storage;

/// <summary>
/// Helper class for generating and managing storage keys with workspace isolation.
/// Keys are structured as: {workspaceId}/{projectId}/{uniqueId}
/// This ensures physical isolation of files between workspaces.
/// </summary>
public static class StorageKeyHelper
{
    /// <summary>
    /// Generates a unique storage key for a file within a project.
    /// The key format ensures workspace isolation: {workspaceId}/{projectId}/{uniqueId}
    /// </summary>
    /// <param name="workspaceId">The workspace GUID.</param>
    /// <param name="projectId">The project GUID.</param>
    /// <param name="fileExtension">Optional file extension to preserve (e.g., ".ifc").</param>
    /// <returns>A unique, workspace-scoped storage key.</returns>
    public static string GenerateKey(Guid workspaceId, Guid projectId, string? fileExtension = null)
    {
        // Generate a cryptographically random ID (not guessable)
        var uniqueId = GenerateUniqueId();

        // Build the key with workspace/project isolation
        var key = $"{workspaceId:N}/{projectId:N}/{uniqueId}";

        // Append extension if provided
        if (!string.IsNullOrEmpty(fileExtension))
        {
            if (!fileExtension.StartsWith('.'))
            {
                fileExtension = "." + fileExtension;
            }
            key += fileExtension;
        }

        return key;
    }

    /// <summary>
    /// Generates a unique storage key for artifacts (processed outputs).
    /// The key format: {workspaceId}/{projectId}/artifacts/{uniqueId}
    /// </summary>
    /// <param name="workspaceId">The workspace GUID.</param>
    /// <param name="projectId">The project GUID.</param>
    /// <param name="artifactType">The type of artifact (e.g., "wexbim", "properties").</param>
    /// <param name="fileExtension">Optional file extension.</param>
    /// <returns>A unique, workspace-scoped artifact storage key.</returns>
    public static string GenerateArtifactKey(Guid workspaceId, Guid projectId, string artifactType, string? fileExtension = null)
    {
        var uniqueId = GenerateUniqueId();
        var key = $"{workspaceId:N}/{projectId:N}/artifacts/{artifactType}/{uniqueId}";

        if (!string.IsNullOrEmpty(fileExtension))
        {
            if (!fileExtension.StartsWith('.'))
            {
                fileExtension = "." + fileExtension;
            }
            key += fileExtension;
        }

        return key;
    }

    /// <summary>
    /// Generates a unique storage key for temporary upload sessions.
    /// The key format: {workspaceId}/{projectId}/uploads/{sessionId}/{uniqueId}
    /// </summary>
    /// <param name="workspaceId">The workspace GUID.</param>
    /// <param name="projectId">The project GUID.</param>
    /// <param name="sessionId">The upload session GUID.</param>
    /// <param name="fileExtension">Optional file extension.</param>
    /// <returns>A unique, workspace-scoped upload storage key.</returns>
    public static string GenerateUploadKey(Guid workspaceId, Guid projectId, Guid sessionId, string? fileExtension = null)
    {
        var uniqueId = GenerateUniqueId();
        var key = $"{workspaceId:N}/{projectId:N}/uploads/{sessionId:N}/{uniqueId}";

        if (!string.IsNullOrEmpty(fileExtension))
        {
            if (!fileExtension.StartsWith('.'))
            {
                fileExtension = "." + fileExtension;
            }
            key += fileExtension;
        }

        return key;
    }

    /// <summary>
    /// Validates that a storage key belongs to the specified workspace.
    /// Prevents cross-workspace access even if someone obtains a key.
    /// </summary>
    /// <param name="storageKey">The storage key to validate.</param>
    /// <param name="workspaceId">The expected workspace GUID.</param>
    /// <returns>True if the key belongs to the workspace; false otherwise.</returns>
    public static bool ValidateWorkspaceAccess(string storageKey, Guid workspaceId)
    {
        if (string.IsNullOrEmpty(storageKey))
            return false;

        var expectedPrefix = $"{workspaceId:N}/";
        return storageKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that a storage key belongs to the specified project.
    /// </summary>
    /// <param name="storageKey">The storage key to validate.</param>
    /// <param name="workspaceId">The expected workspace GUID.</param>
    /// <param name="projectId">The expected project GUID.</param>
    /// <returns>True if the key belongs to the workspace and project; false otherwise.</returns>
    public static bool ValidateProjectAccess(string storageKey, Guid workspaceId, Guid projectId)
    {
        if (string.IsNullOrEmpty(storageKey))
            return false;

        var expectedPrefix = $"{workspaceId:N}/{projectId:N}/";
        return storageKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the workspace ID from a storage key.
    /// </summary>
    /// <param name="storageKey">The storage key.</param>
    /// <returns>The workspace GUID, or null if the key is invalid.</returns>
    public static Guid? ExtractWorkspaceId(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
            return null;

        var parts = storageKey.Split('/');
        if (parts.Length < 1)
            return null;

        if (Guid.TryParseExact(parts[0], "N", out var workspaceId))
            return workspaceId;

        return null;
    }

    /// <summary>
    /// Extracts the project ID from a storage key.
    /// </summary>
    /// <param name="storageKey">The storage key.</param>
    /// <returns>The project GUID, or null if the key is invalid.</returns>
    public static Guid? ExtractProjectId(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
            return null;

        var parts = storageKey.Split('/');
        if (parts.Length < 2)
            return null;

        if (Guid.TryParseExact(parts[1], "N", out var projectId))
            return projectId;

        return null;
    }

    /// <summary>
    /// Generates a cryptographically random unique identifier.
    /// Uses Base64Url encoding for URL-safe characters.
    /// </summary>
    private static string GenerateUniqueId()
    {
        // Generate 16 bytes (128 bits) of randomness
        var bytes = RandomNumberGenerator.GetBytes(16);

        // Convert to Base64Url (URL-safe, no padding)
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

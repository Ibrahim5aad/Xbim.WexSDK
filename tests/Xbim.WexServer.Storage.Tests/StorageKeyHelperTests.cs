using Xbim.WexServer.App.Storage;
using Xunit;

namespace Xbim.WexServer.Storage.Tests;

/// <summary>
/// Tests for StorageKeyHelper ensuring workspace isolation and key generation.
/// </summary>
public class StorageKeyHelperTests
{
    [Fact]
    public void GenerateKey_Returns_Workspace_Scoped_Key()
    {
        // Arrange
        var workspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var projectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        // Act
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectId);

        // Assert
        Assert.StartsWith("11111111222233334444555555555555/", key);
        Assert.Contains("aaaaaaaabbbbccccddddeeeeeeeeeeee/", key);
    }

    [Fact]
    public void GenerateKey_Includes_File_Extension()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // Act
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectId, ".ifc");

        // Assert
        Assert.EndsWith(".ifc", key);
    }

    [Fact]
    public void GenerateKey_Normalizes_Extension_Without_Dot()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // Act
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectId, "ifc");

        // Assert
        Assert.EndsWith(".ifc", key);
    }

    [Fact]
    public void GenerateKey_Produces_Unique_Keys()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // Act
        var keys = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            keys.Add(StorageKeyHelper.GenerateKey(workspaceId, projectId));
        }

        // Assert - All keys should be unique
        Assert.Equal(100, keys.Count);
    }

    [Fact]
    public void GenerateKey_Uses_Cryptographically_Random_Ids()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // Act
        var key1 = StorageKeyHelper.GenerateKey(workspaceId, projectId);
        var key2 = StorageKeyHelper.GenerateKey(workspaceId, projectId);

        // Assert - Keys should be different and not sequential/predictable
        Assert.NotEqual(key1, key2);

        // The unique ID portion should be base64url encoded (no + or /)
        var uniquePart1 = key1.Split('/').Last();
        var uniquePart2 = key2.Split('/').Last();

        Assert.DoesNotContain("+", uniquePart1);
        Assert.DoesNotContain("/", uniquePart1.TrimEnd('.', 'i', 'f', 'c'));
        Assert.DoesNotContain("+", uniquePart2);
    }

    [Fact]
    public void GenerateArtifactKey_Includes_Artifact_Type()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // Act
        var key = StorageKeyHelper.GenerateArtifactKey(workspaceId, projectId, "wexbim", ".wexbim");

        // Assert
        Assert.Contains("/artifacts/wexbim/", key);
        Assert.EndsWith(".wexbim", key);
    }

    [Fact]
    public void GenerateUploadKey_Includes_Session_Id()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Act
        var key = StorageKeyHelper.GenerateUploadKey(workspaceId, projectId, sessionId);

        // Assert
        Assert.Contains("/uploads/", key);
        Assert.Contains(sessionId.ToString("N"), key);
    }

    [Fact]
    public void ValidateWorkspaceAccess_Returns_True_For_Matching_Workspace()
    {
        // Arrange
        var workspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var projectId = Guid.NewGuid();
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectId);

        // Act
        var isValid = StorageKeyHelper.ValidateWorkspaceAccess(key, workspaceId);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateWorkspaceAccess_Returns_False_For_Different_Workspace()
    {
        // Arrange
        var workspaceA = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var workspaceB = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var projectId = Guid.NewGuid();
        var key = StorageKeyHelper.GenerateKey(workspaceA, projectId);

        // Act - User from workspace B tries to access workspace A's file
        var isValid = StorageKeyHelper.ValidateWorkspaceAccess(key, workspaceB);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateWorkspaceAccess_Returns_False_For_Empty_Key()
    {
        // Act
        var isValid = StorageKeyHelper.ValidateWorkspaceAccess("", Guid.NewGuid());

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateWorkspaceAccess_Returns_False_For_Null_Key()
    {
        // Act
        var isValid = StorageKeyHelper.ValidateWorkspaceAccess(null!, Guid.NewGuid());

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateProjectAccess_Returns_True_For_Matching_Project()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectId);

        // Act
        var isValid = StorageKeyHelper.ValidateProjectAccess(key, workspaceId, projectId);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateProjectAccess_Returns_False_For_Different_Project()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var projectB = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectA);

        // Act
        var isValid = StorageKeyHelper.ValidateProjectAccess(key, workspaceId, projectB);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ExtractWorkspaceId_Returns_Correct_Workspace_From_Key()
    {
        // Arrange
        var workspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var projectId = Guid.NewGuid();
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectId);

        // Act
        var extracted = StorageKeyHelper.ExtractWorkspaceId(key);

        // Assert
        Assert.Equal(workspaceId, extracted);
    }

    [Fact]
    public void ExtractWorkspaceId_Returns_Null_For_Invalid_Key()
    {
        // Act
        var extracted = StorageKeyHelper.ExtractWorkspaceId("invalid/key/format");

        // Assert
        Assert.Null(extracted);
    }

    [Fact]
    public void ExtractProjectId_Returns_Correct_Project_From_Key()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var key = StorageKeyHelper.GenerateKey(workspaceId, projectId);

        // Act
        var extracted = StorageKeyHelper.ExtractProjectId(key);

        // Assert
        Assert.Equal(projectId, extracted);
    }

    [Fact]
    public void CrossWorkspace_Access_Denied_Even_With_Known_Key()
    {
        // This test demonstrates the security model:
        // Even if a user from Workspace B somehow obtains a storage key for a file in Workspace A,
        // they cannot access it because the ValidateWorkspaceAccess check will fail.

        // Arrange
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // Workspace A creates a file and gets a storage key
        var storageKey = StorageKeyHelper.GenerateKey(workspaceA, projectId);

        // User from Workspace B tries to use this key
        // (In practice, the API endpoint would call ValidateWorkspaceAccess before allowing access)
        var canAccessFromB = StorageKeyHelper.ValidateWorkspaceAccess(storageKey, workspaceB);

        // Assert
        Assert.False(canAccessFromB, "User from Workspace B should NOT be able to access Workspace A's file");
    }
}

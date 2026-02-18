using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xbim.WexServer.Storage.LocalDisk;
using Xunit;

namespace Xbim.WexServer.Storage.Tests;

/// <summary>
/// Unit tests for LocalDiskStorageProvider.
/// </summary>
public class LocalDiskStorageProviderTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly LocalDiskStorageProvider _provider;

    public LocalDiskStorageProviderTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"Xbim-test-{Guid.NewGuid():N}");
        var options = Options.Create(new LocalDiskStorageOptions
        {
            BasePath = _testBasePath,
            CreateDirectoryIfNotExists = true
        });
        _provider = new LocalDiskStorageProvider(options, NullLogger<LocalDiskStorageProvider>.Instance);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, recursive: true);
        }
    }

    [Fact]
    public void ProviderId_Returns_LocalDisk()
    {
        Assert.Equal("LocalDisk", _provider.ProviderId);
    }

    [Fact]
    public async Task PutAsync_Stores_File_And_Returns_Key()
    {
        // Arrange
        var key = "workspace1/project1/testfile.txt";
        var content = "Hello, World!"u8.ToArray();
        using var stream = new MemoryStream(content);

        // Act
        var result = await _provider.PutAsync(key, stream, "text/plain");

        // Assert
        Assert.Equal(key, result);
        Assert.True(await _provider.ExistsAsync(key));
    }

    [Fact]
    public async Task PutAsync_Creates_Nested_Directories()
    {
        // Arrange
        var key = "deep/nested/path/to/file.txt";
        var content = "test"u8.ToArray();
        using var stream = new MemoryStream(content);

        // Act
        var result = await _provider.PutAsync(key, stream);

        // Assert
        Assert.Equal(key, result);
        Assert.True(await _provider.ExistsAsync(key));
    }

    [Fact]
    public async Task PutAsync_Rejects_Duplicate_Keys_For_Concurrency_Safety()
    {
        // Arrange
        var key = "workspace1/project1/unique.txt";
        var content = "first"u8.ToArray();
        using var stream1 = new MemoryStream(content);
        using var stream2 = new MemoryStream(content);

        await _provider.PutAsync(key, stream1);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _provider.PutAsync(key, stream2));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task OpenReadAsync_Returns_Stream_For_Existing_File()
    {
        // Arrange
        var key = "workspace1/read-test.txt";
        var content = "Read me!"u8.ToArray();
        using var writeStream = new MemoryStream(content);
        await _provider.PutAsync(key, writeStream, "text/plain");

        // Act
        using var readStream = await _provider.OpenReadAsync(key);

        // Assert
        Assert.NotNull(readStream);
        using var reader = new StreamReader(readStream);
        var result = await reader.ReadToEndAsync();
        Assert.Equal("Read me!", result);
    }

    [Fact]
    public async Task OpenReadAsync_Returns_Null_For_NonExistent_File()
    {
        // Act
        var stream = await _provider.OpenReadAsync("does-not-exist.txt");

        // Assert
        Assert.Null(stream);
    }

    [Fact]
    public async Task OpenReadAsync_Supports_Streaming_Large_Files()
    {
        // Arrange - Create a 1MB file
        var key = "workspace1/large-file.bin";
        var largeContent = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeContent);
        using var writeStream = new MemoryStream(largeContent);
        await _provider.PutAsync(key, writeStream);

        // Act - Read in chunks to verify streaming works
        using var readStream = await _provider.OpenReadAsync(key);
        Assert.NotNull(readStream);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await readStream.ReadAsync(buffer)) > 0)
        {
            totalRead += bytesRead;
        }

        // Assert
        Assert.Equal(largeContent.Length, totalRead);
    }

    [Fact]
    public async Task DeleteAsync_Removes_Existing_File()
    {
        // Arrange
        var key = "workspace1/delete-me.txt";
        var content = "Delete this"u8.ToArray();
        using var stream = new MemoryStream(content);
        await _provider.PutAsync(key, stream);
        Assert.True(await _provider.ExistsAsync(key));

        // Act
        var result = await _provider.DeleteAsync(key);

        // Assert
        Assert.True(result);
        Assert.False(await _provider.ExistsAsync(key));
    }

    [Fact]
    public async Task DeleteAsync_Returns_False_For_NonExistent_File()
    {
        // Act
        var result = await _provider.DeleteAsync("nonexistent.txt");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_Returns_True_For_Existing_File()
    {
        // Arrange
        var key = "workspace1/exists.txt";
        using var stream = new MemoryStream("test"u8.ToArray());
        await _provider.PutAsync(key, stream);

        // Act
        var exists = await _provider.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_For_NonExistent_File()
    {
        // Act
        var exists = await _provider.ExistsAsync("nope.txt");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task GetSizeAsync_Returns_Correct_Size()
    {
        // Arrange
        var key = "workspace1/sized.txt";
        var content = "1234567890"u8.ToArray(); // 10 bytes
        using var stream = new MemoryStream(content);
        await _provider.PutAsync(key, stream);

        // Act
        var size = await _provider.GetSizeAsync(key);

        // Assert
        Assert.Equal(10, size);
    }

    [Fact]
    public async Task GetSizeAsync_Returns_Null_For_NonExistent_File()
    {
        // Act
        var size = await _provider.GetSizeAsync("missing.txt");

        // Assert
        Assert.Null(size);
    }

    [Fact]
    public async Task Storage_Maintains_Workspace_Isolation_Through_Key_Structure()
    {
        // Arrange - Store files in different workspace paths
        var workspace1Key = "workspace-aaa/project-1/file.txt";
        var workspace2Key = "workspace-bbb/project-1/file.txt";

        using var stream1 = new MemoryStream("workspace 1 data"u8.ToArray());
        using var stream2 = new MemoryStream("workspace 2 data"u8.ToArray());

        await _provider.PutAsync(workspace1Key, stream1);
        await _provider.PutAsync(workspace2Key, stream2);

        // Act & Assert - Each workspace can read its own file
        using var read1 = await _provider.OpenReadAsync(workspace1Key);
        using var read2 = await _provider.OpenReadAsync(workspace2Key);

        Assert.NotNull(read1);
        Assert.NotNull(read2);

        using var reader1 = new StreamReader(read1);
        using var reader2 = new StreamReader(read2);

        Assert.Equal("workspace 1 data", await reader1.ReadToEndAsync());
        Assert.Equal("workspace 2 data", await reader2.ReadToEndAsync());

        // Workspace isolation is enforced by the key structure
        // A workspace cannot access another workspace's files unless they know the full key
        // The application layer (API endpoints) enforces that users can only use keys for their workspace
    }

    [Fact]
    public async Task PutAsync_Prevents_Directory_Traversal_Attacks()
    {
        // Arrange - Attempt to use path traversal in key
        var maliciousKey = "../../../etc/passwd";
        using var stream = new MemoryStream("malicious"u8.ToArray());

        // Act - The path traversal sequences are sanitized and the file is stored safely
        await _provider.PutAsync(maliciousKey, stream);

        // Assert - File is stored within the base path (sanitized key)
        // The ".." sequences are stripped out, leaving "etc/passwd" which is safe
        Assert.True(await _provider.ExistsAsync(maliciousKey));

        // Verify file is NOT actually stored outside the base path
        // The file should be in {basePath}/etc/passwd, not /etc/passwd
        var actualPath = Path.Combine(_testBasePath, "etc", "passwd");
        Assert.True(File.Exists(actualPath), "File should be stored within base path");

        // Verify no file was created outside base path
        var dangerousPath = Path.Combine(Path.GetTempPath(), "..", "..", "..", "etc", "passwd");
        Assert.False(File.Exists(dangerousPath), "File should NOT be created outside base path");
    }

    [Fact]
    public async Task PutAsync_Allows_Safe_Nested_Paths()
    {
        // Arrange - Use a safe nested path
        var safeKey = "workspace/project/subfolder/file.txt";
        using var stream = new MemoryStream("safe content"u8.ToArray());

        // Act
        await _provider.PutAsync(safeKey, stream);

        // Assert - File should be stored successfully
        Assert.True(await _provider.ExistsAsync(safeKey));
    }
}

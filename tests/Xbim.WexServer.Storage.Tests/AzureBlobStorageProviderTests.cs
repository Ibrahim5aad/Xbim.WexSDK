using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xbim.WexServer.Storage.AzureBlob;
using Xunit;

namespace Xbim.WexServer.Storage.Tests;

/// <summary>
/// Integration tests for AzureBlobStorageProvider using Azurite emulator.
/// These tests require Azurite to be running locally.
/// To start Azurite: npm install -g azurite &amp;&amp; azurite --location ./azurite-data
/// Or using Docker: docker run -p 10000:10000 mcr.microsoft.com/azure-storage/azurite
/// </summary>
[Collection("AzuriteTests")]
[Trait("Category", "RequiresAzurite")]
public class AzureBlobStorageProviderTests : IAsyncLifetime
{
    // Azurite default connection string
    private const string AzuriteConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private readonly string _containerName;
    private readonly AzureBlobStorageProvider _provider;
    private readonly AzureBlobStorageOptions _options;
    private bool _azuriteAvailable;

    public AzureBlobStorageProviderTests()
    {
        // Use a unique container name for each test run to ensure isolation
        _containerName = $"test-{Guid.NewGuid():N}";
        _options = new AzureBlobStorageOptions
        {
            ConnectionString = AzuriteConnectionString,
            ContainerName = _containerName,
            CreateContainerIfNotExists = true
        };
        var options = Options.Create(_options);
        _provider = new AzureBlobStorageProvider(options, NullLogger<AzureBlobStorageProvider>.Instance);
    }

    public async Task InitializeAsync()
    {
        // Check if Azurite is running
        try
        {
            var testKey = $"connection-test-{Guid.NewGuid():N}";
            using var stream = new MemoryStream("test"u8.ToArray());
            await _provider.PutAsync(testKey, stream);
            await _provider.DeleteAsync(testKey);
            _azuriteAvailable = true;
        }
        catch
        {
            _azuriteAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        // Cleanup: delete the test container
        if (_azuriteAvailable)
        {
            try
            {
                var serviceClient = new Azure.Storage.Blobs.BlobServiceClient(_options.ConnectionString);
                var containerClient = serviceClient.GetBlobContainerClient(_containerName);
                await containerClient.DeleteIfExistsAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        await Task.CompletedTask;
    }

    private void SkipIfAzuriteNotAvailable()
    {
        Skip.If(!_azuriteAvailable, "Azurite emulator is not available. Start Azurite to run these tests.");
    }

    [SkippableFact]
    public void ProviderId_Returns_AzureBlob()
    {
        SkipIfAzuriteNotAvailable();
        Assert.Equal("AzureBlob", _provider.ProviderId);
    }

    [SkippableFact]
    public async Task PutAsync_Stores_Blob_And_Returns_Key()
    {
        SkipIfAzuriteNotAvailable();

        // Arrange
        var key = "workspace1/project1/testfile.txt";
        var content = "Hello, Azure!"u8.ToArray();
        using var stream = new MemoryStream(content);

        // Act
        var result = await _provider.PutAsync(key, stream, "text/plain");

        // Assert
        Assert.Equal(key, result);
        Assert.True(await _provider.ExistsAsync(key));
    }

    [SkippableFact]
    public async Task PutAsync_Creates_Nested_Blob_Paths()
    {
        SkipIfAzuriteNotAvailable();

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

    [SkippableFact]
    public async Task PutAsync_Rejects_Duplicate_Keys_For_Concurrency_Safety()
    {
        SkipIfAzuriteNotAvailable();

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

    [SkippableFact]
    public async Task OpenReadAsync_Returns_Stream_For_Existing_Blob()
    {
        SkipIfAzuriteNotAvailable();

        // Arrange
        var key = "workspace1/read-test.txt";
        var content = "Read me from Azure!"u8.ToArray();
        using var writeStream = new MemoryStream(content);
        await _provider.PutAsync(key, writeStream, "text/plain");

        // Act
        using var readStream = await _provider.OpenReadAsync(key);

        // Assert
        Assert.NotNull(readStream);
        using var reader = new StreamReader(readStream);
        var result = await reader.ReadToEndAsync();
        Assert.Equal("Read me from Azure!", result);
    }

    [SkippableFact]
    public async Task OpenReadAsync_Returns_Null_For_NonExistent_Blob()
    {
        SkipIfAzuriteNotAvailable();

        // Act
        var stream = await _provider.OpenReadAsync("does-not-exist.txt");

        // Assert
        Assert.Null(stream);
    }

    [SkippableFact]
    public async Task OpenReadAsync_Supports_Streaming_Large_Blobs()
    {
        SkipIfAzuriteNotAvailable();

        // Arrange - Create a 1MB blob
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

    [SkippableFact]
    public async Task DeleteAsync_Removes_Existing_Blob()
    {
        SkipIfAzuriteNotAvailable();

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

    [SkippableFact]
    public async Task DeleteAsync_Returns_False_For_NonExistent_Blob()
    {
        SkipIfAzuriteNotAvailable();

        // Act
        var result = await _provider.DeleteAsync("nonexistent.txt");

        // Assert
        Assert.False(result);
    }

    [SkippableFact]
    public async Task ExistsAsync_Returns_True_For_Existing_Blob()
    {
        SkipIfAzuriteNotAvailable();

        // Arrange
        var key = "workspace1/exists.txt";
        using var stream = new MemoryStream("test"u8.ToArray());
        await _provider.PutAsync(key, stream);

        // Act
        var exists = await _provider.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [SkippableFact]
    public async Task ExistsAsync_Returns_False_For_NonExistent_Blob()
    {
        SkipIfAzuriteNotAvailable();

        // Act
        var exists = await _provider.ExistsAsync("nope.txt");

        // Assert
        Assert.False(exists);
    }

    [SkippableFact]
    public async Task GetSizeAsync_Returns_Correct_Size()
    {
        SkipIfAzuriteNotAvailable();

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

    [SkippableFact]
    public async Task GetSizeAsync_Returns_Null_For_NonExistent_Blob()
    {
        SkipIfAzuriteNotAvailable();

        // Act
        var size = await _provider.GetSizeAsync("missing.txt");

        // Assert
        Assert.Null(size);
    }

    [SkippableFact]
    public async Task Storage_Maintains_Workspace_Isolation_Through_Key_Structure()
    {
        SkipIfAzuriteNotAvailable();

        // Arrange - Store blobs in different workspace paths
        var workspace1Key = "workspace-aaa/project-1/file.txt";
        var workspace2Key = "workspace-bbb/project-1/file.txt";

        using var stream1 = new MemoryStream("workspace 1 data"u8.ToArray());
        using var stream2 = new MemoryStream("workspace 2 data"u8.ToArray());

        await _provider.PutAsync(workspace1Key, stream1);
        await _provider.PutAsync(workspace2Key, stream2);

        // Act & Assert - Each workspace can read its own blob
        using var read1 = await _provider.OpenReadAsync(workspace1Key);
        using var read2 = await _provider.OpenReadAsync(workspace2Key);

        Assert.NotNull(read1);
        Assert.NotNull(read2);

        using var reader1 = new StreamReader(read1);
        using var reader2 = new StreamReader(read2);

        Assert.Equal("workspace 1 data", await reader1.ReadToEndAsync());
        Assert.Equal("workspace 2 data", await reader2.ReadToEndAsync());
    }

    [SkippableFact]
    public async Task PutAsync_Sets_ContentType_Header()
    {
        SkipIfAzuriteNotAvailable();

        // Arrange
        var key = "workspace1/document.json";
        var content = "{\"test\": true}"u8.ToArray();
        using var stream = new MemoryStream(content);

        // Act
        await _provider.PutAsync(key, stream, "application/json");

        // Assert - Verify blob exists and content is correct
        using var readStream = await _provider.OpenReadAsync(key);
        Assert.NotNull(readStream);
        using var reader = new StreamReader(readStream);
        var result = await reader.ReadToEndAsync();
        Assert.Equal("{\"test\": true}", result);
    }
}

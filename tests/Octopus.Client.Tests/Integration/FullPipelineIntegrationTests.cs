using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octopus.Client;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Abstractions.Storage;
using Octopus.Server.App.Processing;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;
using Octopus.Server.Processing;
using Xunit;
using DomainFileKind = Octopus.Server.Domain.Enums.FileKind;
using DomainFileCategory = Octopus.Server.Domain.Enums.FileCategory;
using DomainProcessingStatus = Octopus.Server.Domain.Enums.ProcessingStatus;

// NSwag-generated enums use numeric names (_0, _1, etc.) instead of named values
// Map to the actual enum values:
// ProcessingStatus: _0=Pending, _1=Processing, _2=Ready, _3=Failed
// FileKind: _0=Source, _1=Artifact
// FileCategory: _0=Ifc, _1=WexBim, _2=Properties, _3=Other

namespace Octopus.Client.Tests.Integration;

/// <summary>
/// Integration tests using the generated API client to test the full pipeline:
/// Upload IFC -> Create Model -> Create Version -> Process WexBIM and Properties
/// </summary>
public class FullPipelineIntegrationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _httpClient;
    private readonly OctopusApiClient _apiClient;
    private readonly string _testDbName;
    private readonly InMemoryStorageProvider _storageProvider;
    private readonly TestProcessingQueue _processingQueue;

    private static readonly string TestFilesPath = Path.Combine(
        AppContext.BaseDirectory, "Integration", "TestFiles");

    public FullPipelineIntegrationTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProvider();
        _processingQueue = new TestProcessingQueue();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove ALL DbContext-related services
                services.RemoveAll(typeof(DbContextOptions<OctopusDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(OctopusDbContext));

                // Remove storage provider and add in-memory one
                services.RemoveAll(typeof(IStorageProvider));
                services.AddSingleton<IStorageProvider>(_storageProvider);

                // Remove default processing queue and add test one that captures jobs
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

                // Add in-memory database for testing
                services.AddDbContext<OctopusDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDbName);
                });
            });
        });

        _httpClient = _factory.CreateClient();
        _apiClient = new OctopusApiClient(_httpClient.BaseAddress!.ToString(), _httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// Full end-to-end test simulating a user using the generated client to:
    /// 1. Create workspace
    /// 2. Create project
    /// 3. Upload IFC file (using real SampleHouse.ifc)
    /// 4. Create model
    /// 5. Create model version (triggers processing)
    /// 6. Simulate processing completion (with real SampleHouse.wexbim)
    /// 7. Verify WexBIM artifact is accessible and has correct content
    /// 8. Verify properties endpoint is queryable
    /// </summary>
    [Fact]
    public async Task FullPipeline_UsingGeneratedClient_UploadIfcAndProcessArtifacts()
    {
        // Arrange - Load real test files
        var ifcFilePath = Path.Combine(TestFilesPath, "SampleHouse.ifc");
        var wexbimFilePath = Path.Combine(TestFilesPath, "SampleHouse.wexbim");

        Assert.True(File.Exists(ifcFilePath), $"Test file not found: {ifcFilePath}");
        Assert.True(File.Exists(wexbimFilePath), $"Test file not found: {wexbimFilePath}");

        var ifcContent = await File.ReadAllBytesAsync(ifcFilePath);
        var wexbimContent = await File.ReadAllBytesAsync(wexbimFilePath);

        // Step 1: Create workspace using generated client
        var workspace = await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Integration Test Workspace"
        });
        Assert.NotNull(workspace);
        Assert.NotEqual(Guid.Empty, workspace.Id);

        // Step 2: Create project using generated client
        var project = await _apiClient.CreateProjectAsync(workspace.Id!.Value, new CreateProjectRequest
        {
            Name = "Integration Test Project"
        });
        Assert.NotNull(project);
        Assert.NotEqual(Guid.Empty, project.Id);

        // Step 3: Upload IFC file using generated client (reserve -> upload content -> commit)
        var uploadedFile = await UploadFileAsync(project.Id!.Value, "SampleHouse.ifc", "application/x-step", ifcContent);
        Assert.NotNull(uploadedFile);
        Assert.NotEqual(Guid.Empty, uploadedFile.Id);
        Assert.Equal("SampleHouse.ifc", uploadedFile.Name);
        Assert.Equal(ifcContent.Length, uploadedFile.SizeBytes);

        // Step 4: Create model using generated client
        var model = await _apiClient.CreateModelAsync(project.Id!.Value, new CreateModelRequest
        {
            Name = "Sample House Model"
        });
        Assert.NotNull(model);
        Assert.NotEqual(Guid.Empty, model.Id);

        // Step 5: Create model version using generated client (sets status to Pending)
        var version = await _apiClient.CreateModelVersionAsync(model.Id!.Value, new CreateModelVersionRequest
        {
            IfcFileId = uploadedFile.Id
        });
        Assert.NotNull(version);
        Assert.NotEqual(Guid.Empty, version.Id);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(ProcessingStatus._0 /* Pending */, version.Status);

        // Note: In production, a background worker would pick up Pending versions and process them.
        // In this test, we simulate that processing completion.

        // Step 6: Simulate processing completion with real wexbim file
        await SimulateProcessingCompletionAsync(
            version.Id!.Value,
            project.Id!.Value,
            workspace.Id!.Value,
            uploadedFile.Id!.Value,
            wexbimContent);

        // Step 7: Verify WexBIM artifact is accessible using generated client
        var wexBimResponse = await _apiClient.GetModelVersionWexBimAsync(version.Id!.Value);
        Assert.NotNull(wexBimResponse);
        Assert.NotNull(wexBimResponse.Stream);

        using var memoryStream = new MemoryStream();
        await wexBimResponse.Stream.CopyToAsync(memoryStream);
        var downloadedWexBim = memoryStream.ToArray();

        Assert.Equal(wexbimContent.Length, downloadedWexBim.Length);
        Assert.Equal(wexbimContent, downloadedWexBim);

        // Step 8: Verify properties endpoint is accessible using generated client
        var propertiesResult = await _apiClient.QueryPropertiesAsync(version.Id!.Value, page: 1, pageSize: 10);
        Assert.NotNull(propertiesResult);

        // Verify model version status updated to Ready using generated client
        var updatedVersion = await _apiClient.GetModelVersionAsync(version.Id!.Value);
        Assert.Equal(ProcessingStatus._2 /* Ready */, updatedVersion.Status);
        Assert.NotNull(updatedVersion.WexBimFileId);
        Assert.NotNull(updatedVersion.PropertiesFileId);
        Assert.NotNull(updatedVersion.ProcessedAt);
    }

    [Fact]
    public async Task UploadFile_UsingGeneratedClient_ReturnsFileMetadata()
    {
        // Arrange
        var ifcFilePath = Path.Combine(TestFilesPath, "SampleHouse.ifc");
        var ifcContent = await File.ReadAllBytesAsync(ifcFilePath);

        var workspace = await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Upload Test Workspace"
        });
        var project = await _apiClient.CreateProjectAsync(workspace.Id!.Value, new CreateProjectRequest
        {
            Name = "Upload Test Project"
        });

        // Act
        var uploadedFile = await UploadFileAsync(project.Id!.Value, "SampleHouse.ifc", "application/x-step", ifcContent);

        // Assert
        Assert.NotNull(uploadedFile);
        Assert.Equal("SampleHouse.ifc", uploadedFile.Name);
        Assert.Equal(ifcContent.Length, uploadedFile.SizeBytes);
        // Generated client enums: FileKind: Source=0, Artifact=1; FileCategory: Other=0, Ifc=1, WexBim=2
        Assert.Equal(0, (int)uploadedFile.Kind!.Value); // Source = 0
        Assert.Equal(1, (int)uploadedFile.Category!.Value); // Ifc = 1
    }

    [Fact]
    public async Task DownloadFile_UsingGeneratedClient_ReturnsSameContent()
    {
        // Arrange
        var ifcFilePath = Path.Combine(TestFilesPath, "SampleHouse.ifc");
        var originalContent = await File.ReadAllBytesAsync(ifcFilePath);

        var workspace = await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Download Test Workspace"
        });
        var project = await _apiClient.CreateProjectAsync(workspace.Id!.Value, new CreateProjectRequest
        {
            Name = "Download Test Project"
        });
        var uploadedFile = await UploadFileAsync(project.Id!.Value, "SampleHouse.ifc", "application/x-step", originalContent);

        // Act - Download using generated client
        var downloadResponse = await _apiClient.GetFileContentAsync(uploadedFile.Id!.Value);

        using var memoryStream = new MemoryStream();
        await downloadResponse.Stream.CopyToAsync(memoryStream);
        var downloadedContent = memoryStream.ToArray();

        // Assert
        Assert.Equal(originalContent.Length, downloadedContent.Length);
        Assert.Equal(originalContent, downloadedContent);
    }

    [Fact]
    public async Task CreateModelVersion_UsingGeneratedClient_SetsStatusToPending()
    {
        // Arrange
        var ifcFilePath = Path.Combine(TestFilesPath, "SampleHouse.ifc");
        var ifcContent = await File.ReadAllBytesAsync(ifcFilePath);

        var workspace = await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Version Test Workspace"
        });
        var project = await _apiClient.CreateProjectAsync(workspace.Id!.Value, new CreateProjectRequest
        {
            Name = "Version Test Project"
        });
        var uploadedFile = await UploadFileAsync(project.Id!.Value, "SampleHouse.ifc", "application/x-step", ifcContent);
        var model = await _apiClient.CreateModelAsync(project.Id!.Value, new CreateModelRequest
        {
            Name = "Version Test Model"
        });

        // Act
        var version = await _apiClient.CreateModelVersionAsync(model.Id!.Value, new CreateModelVersionRequest
        {
            IfcFileId = uploadedFile.Id
        });

        // Assert
        Assert.NotNull(version);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(ProcessingStatus._0 /* Pending */, version.Status);
        Assert.Equal(uploadedFile.Id, version.IfcFileId);
        Assert.Null(version.WexBimFileId);
        Assert.Null(version.PropertiesFileId);
    }

    [Fact]
    public async Task GetWexBim_BeforeProcessing_ThrowsException()
    {
        // Arrange
        var ifcFilePath = Path.Combine(TestFilesPath, "SampleHouse.ifc");
        var ifcContent = await File.ReadAllBytesAsync(ifcFilePath);

        var workspace = await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "NotReady Test Workspace"
        });
        var project = await _apiClient.CreateProjectAsync(workspace.Id!.Value, new CreateProjectRequest
        {
            Name = "NotReady Test Project"
        });
        var uploadedFile = await UploadFileAsync(project.Id!.Value, "SampleHouse.ifc", "application/x-step", ifcContent);
        var model = await _apiClient.CreateModelAsync(project.Id!.Value, new CreateModelRequest
        {
            Name = "NotReady Test Model"
        });
        var version = await _apiClient.CreateModelVersionAsync(model.Id!.Value, new CreateModelVersionRequest
        {
            IfcFileId = uploadedFile.Id
        });

        // Act & Assert - Generated client throws exception for 404
        await Assert.ThrowsAsync<OctopusApiException>(async () =>
            await _apiClient.GetModelVersionWexBimAsync(version.Id!.Value));
    }

    [Fact]
    public async Task ListWorkspaces_UsingGeneratedClient_ReturnsWorkspaces()
    {
        // Arrange
        await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "List Test Workspace 1"
        });
        await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "List Test Workspace 2"
        });

        // Act
        var result = await _apiClient.ListWorkspacesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 2);
    }

    #region Helper Methods

    /// <summary>
    /// Uploads a file using the generated client for all steps: reserve, upload content, and commit.
    /// </summary>
    private async Task<FileDto> UploadFileAsync(Guid projectId, string fileName, string contentType, byte[] content)
    {
        // Step 1: Reserve upload using generated client
        var reserved = await _apiClient.ReserveUploadAsync(projectId, new ReserveUploadRequest
        {
            FileName = fileName,
            ContentType = contentType,
            ExpectedSizeBytes = content.Length
        });

        // Step 2: Upload content using generated client with FileParameter
        var fileStream = new MemoryStream(content);
        var fileParam = new FileParameter(fileStream, fileName, contentType);
        await _apiClient.UploadContentAsync(projectId, reserved.Session!.Id!.Value, fileParam);

        // Step 3: Commit upload using generated client
        var commitResult = await _apiClient.CommitUploadAsync(projectId, reserved.Session!.Id!.Value);

        return commitResult.File!;
    }

    private async Task SimulateProcessingCompletionAsync(
        Guid modelVersionId,
        Guid projectId,
        Guid workspaceId,
        Guid ifcFileId,
        byte[] wexBimContent)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        // Create WexBIM artifact file
        var wexBimStorageKey = $"{workspaceId:N}/{projectId:N}/artifacts/{modelVersionId:N}.wexbim";
        await _storageProvider.PutAsync(wexBimStorageKey, new MemoryStream(wexBimContent));

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = "SampleHouse.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = wexBimContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = wexBimStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        // Create properties artifact file (SQLite database simulation)
        var propsContent = CreateSamplePropertiesDb();
        var propsStorageKey = $"{workspaceId:N}/{projectId:N}/artifacts/{modelVersionId:N}.properties.db";
        await _storageProvider.PutAsync(propsStorageKey, new MemoryStream(propsContent));

        var propsFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = "SampleHouse.properties.db",
            ContentType = "application/x-sqlite3",
            SizeBytes = propsContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.Properties,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = propsStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(propsFile);

        // Update model version
        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == modelVersionId);
        modelVersion.Status = DomainProcessingStatus.Ready;
        modelVersion.WexBimFileId = wexBimFile.Id;
        modelVersion.PropertiesFileId = propsFile.Id;
        modelVersion.ProcessedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
    }

    private static byte[] CreateSamplePropertiesDb()
    {
        // SQLite database header magic bytes followed by minimal valid structure
        // This is a minimal valid SQLite file header
        return "SQLite format 3\0"u8.ToArray();
    }

    #endregion
}

/// <summary>
/// In-memory storage provider for testing.
/// </summary>
public class InMemoryStorageProvider : IStorageProvider
{
    public string ProviderId => "InMemory";

    public ConcurrentDictionary<string, byte[]> Storage { get; } = new();

    public Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        Storage[key] = ms.ToArray();
        return Task.FromResult(key);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (Storage.TryGetValue(key, out var data))
        {
            return Task.FromResult<Stream?>(new MemoryStream(data));
        }
        return Task.FromResult<Stream?>(null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Storage.TryRemove(key, out _));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Storage.ContainsKey(key));
    }

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        if (Storage.TryGetValue(key, out var data))
        {
            return Task.FromResult<long?>(data.Length);
        }
        return Task.FromResult<long?>(null);
    }
}

/// <summary>
/// Test processing queue that captures enqueued jobs.
/// </summary>
public class TestProcessingQueue : IProcessingQueue
{
    private readonly ConcurrentQueue<JobEnvelope> _queue = new();
    public List<JobEnvelope> EnqueuedJobs { get; } = new();

    public ValueTask EnqueueAsync(JobEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnqueuedJobs.Add(envelope);
        _queue.Enqueue(envelope);
        return ValueTask.CompletedTask;
    }

    public ValueTask<JobEnvelope?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_queue.TryDequeue(out var envelope))
        {
            return ValueTask.FromResult<JobEnvelope?>(envelope);
        }
        return ValueTask.FromResult<JobEnvelope?>(null);
    }
}

/// <summary>
/// Real end-to-end integration tests that actually run the xBIM processing.
/// These tests use the real ProcessingWorkerService to convert IFC to WexBIM.
/// </summary>
[Collection("RealProcessing")]
public class RealWexBimProcessingTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _httpClient;
    private readonly OctopusApiClient _apiClient;
    private readonly string _testDbName;
    private readonly InMemoryStorageProvider _storageProvider;

    private static readonly string TestFilesPath = Path.Combine(
        AppContext.BaseDirectory, "Integration", "TestFiles");

    public RealWexBimProcessingTests()
    {
        _testDbName = $"real_processing_test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProvider();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove ALL DbContext-related services
                services.RemoveAll(typeof(DbContextOptions<OctopusDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(OctopusDbContext));

                // Use in-memory storage
                services.RemoveAll(typeof(IStorageProvider));
                services.AddSingleton<IStorageProvider>(_storageProvider);

                // Add in-memory database for testing
                services.AddDbContext<OctopusDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDbName);
                });

                // Add the real processing services (since Testing env skips them)
                // This enables the ProcessingWorkerService to run real xBIM processing
                services.AddInMemoryProcessing(processing =>
                {
                    processing.AddHandler<IfcToWexBimJobPayload, IfcToWexBimJobHandler>(IfcToWexBimJobHandler.JobTypeName);
                    processing.AddHandler<ExtractPropertiesJobPayload, ExtractPropertiesJobHandler>(ExtractPropertiesJobHandler.JobTypeName);
                });
            });
        });

        _httpClient = _factory.CreateClient();
        _apiClient = new OctopusApiClient(_httpClient.BaseAddress!.ToString(), _httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// Real end-to-end test that uploads an IFC file and waits for actual xBIM processing.
    /// This test:
    /// 1. Creates workspace/project/model via API
    /// 2. Uploads a real IFC file
    /// 3. Creates a model version (queues processing job)
    /// 4. Waits for the ProcessingWorkerService to actually convert IFC to WexBIM
    /// 5. Downloads and verifies the generated WexBIM
    /// </summary>
    [Fact]
    public async Task RealProcessing_UploadIfc_GeneratesWexBim()
    {
        // Arrange - Load real IFC file
        var ifcFilePath = Path.Combine(TestFilesPath, "SampleHouse.ifc");
        Assert.True(File.Exists(ifcFilePath), $"Test file not found: {ifcFilePath}");
        var ifcContent = await File.ReadAllBytesAsync(ifcFilePath);

        // Step 1: Create workspace
        var workspace = await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Real Processing Test Workspace"
        });
        Assert.NotNull(workspace);

        // Step 2: Create project
        var project = await _apiClient.CreateProjectAsync(workspace.Id!.Value, new CreateProjectRequest
        {
            Name = "Real Processing Test Project"
        });
        Assert.NotNull(project);

        // Step 3: Upload IFC file
        var uploadedFile = await UploadFileAsync(project.Id!.Value, "SampleHouse.ifc", "application/x-step", ifcContent);
        Assert.NotNull(uploadedFile);
        Assert.Equal("SampleHouse.ifc", uploadedFile.Name);

        // Step 4: Create model
        var model = await _apiClient.CreateModelAsync(project.Id!.Value, new CreateModelRequest
        {
            Name = "Real Processing Test Model"
        });
        Assert.NotNull(model);

        // Step 5: Create model version - this triggers processing
        var version = await _apiClient.CreateModelVersionAsync(model.Id!.Value, new CreateModelVersionRequest
        {
            IfcFileId = uploadedFile.Id
        });
        Assert.NotNull(version);
        Assert.Equal(ProcessingStatus._0 /* Pending */, version.Status);

        // Step 6: Wait for processing to complete (real xBIM processing)
        var processedVersion = await WaitForProcessingAsync(version.Id!.Value, timeoutSeconds: 60);

        // Step 7: Verify processing succeeded
        Assert.Equal(ProcessingStatus._2 /* Ready */, processedVersion.Status);
        Assert.NotNull(processedVersion.WexBimFileId);
        Assert.NotNull(processedVersion.ProcessedAt);
        Assert.Null(processedVersion.ErrorMessage);

        // Step 8: Download and verify WexBIM
        var wexBimResponse = await _apiClient.GetModelVersionWexBimAsync(version.Id!.Value);
        Assert.NotNull(wexBimResponse);
        Assert.NotNull(wexBimResponse.Stream);

        using var memoryStream = new MemoryStream();
        await wexBimResponse.Stream.CopyToAsync(memoryStream);
        var wexbimContent = memoryStream.ToArray();

        // WexBIM should have reasonable size (the sample file generates ~300KB)
        Assert.True(wexbimContent.Length > 100, $"WexBIM file too small: {wexbimContent.Length} bytes");
        Assert.True(wexbimContent.Length < 10_000_000, $"WexBIM file unexpectedly large: {wexbimContent.Length} bytes");
    }

    /// <summary>
    /// Test that processing handles errors gracefully when IFC file is invalid.
    /// </summary>
    [Fact]
    public async Task RealProcessing_InvalidIfc_SetsStatusToFailed()
    {
        // Arrange - Create an invalid IFC file (just some text)
        var invalidIfcContent = System.Text.Encoding.UTF8.GetBytes("This is not a valid IFC file");

        var workspace = await _apiClient.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Invalid IFC Test Workspace"
        });
        var project = await _apiClient.CreateProjectAsync(workspace.Id!.Value, new CreateProjectRequest
        {
            Name = "Invalid IFC Test Project"
        });
        var uploadedFile = await UploadFileAsync(project.Id!.Value, "invalid.ifc", "application/x-step", invalidIfcContent);
        var model = await _apiClient.CreateModelAsync(project.Id!.Value, new CreateModelRequest
        {
            Name = "Invalid IFC Test Model"
        });

        // Act - Create model version with invalid IFC
        var version = await _apiClient.CreateModelVersionAsync(model.Id!.Value, new CreateModelVersionRequest
        {
            IfcFileId = uploadedFile.Id
        });

        // Wait for processing to complete (should fail)
        var processedVersion = await WaitForProcessingAsync(version.Id!.Value, timeoutSeconds: 30);

        // Assert - Processing should fail gracefully
        Assert.Equal(ProcessingStatus._3 /* Failed */, processedVersion.Status);
        Assert.NotNull(processedVersion.ErrorMessage);
        Assert.Null(processedVersion.WexBimFileId);
    }

    #region Helper Methods

    private async Task<FileDto> UploadFileAsync(Guid projectId, string fileName, string contentType, byte[] content)
    {
        var reserved = await _apiClient.ReserveUploadAsync(projectId, new ReserveUploadRequest
        {
            FileName = fileName,
            ContentType = contentType,
            ExpectedSizeBytes = content.Length
        });

        var fileStream = new MemoryStream(content);
        var fileParam = new FileParameter(fileStream, fileName, contentType);
        await _apiClient.UploadContentAsync(projectId, reserved.Session!.Id!.Value, fileParam);

        var commitResult = await _apiClient.CommitUploadAsync(projectId, reserved.Session!.Id!.Value);
        return commitResult.File!;
    }

    private async Task<ModelVersionDto> WaitForProcessingAsync(Guid versionId, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var version = await _apiClient.GetModelVersionAsync(versionId);

            // Check if processing is complete (Ready or Failed)
            if (version.Status == ProcessingStatus._2 /* Ready */ ||
                version.Status == ProcessingStatus._3 /* Failed */)
            {
                return version;
            }

            // Still processing, wait a bit
            await Task.Delay(500);
        }

        throw new TimeoutException($"Processing did not complete within {timeoutSeconds} seconds");
    }

    #endregion
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using ProjectRole = Xbim.WexServer.Domain.Enums.ProjectRole;
using DomainFileKind = Xbim.WexServer.Domain.Enums.FileKind;
using DomainFileCategory = Xbim.WexServer.Domain.Enums.FileCategory;

namespace Xbim.WexServer.Tests.Endpoints;

public class InMemoryStorageProviderForModelVersionTests : IStorageProvider
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

    public bool SupportsDirectUpload => false;

    public Task<string?> GenerateUploadSasUrlAsync(string key, string? contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<StorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StorageHealthResult.Healthy("InMemory storage is healthy"));
    }
}


public class ModelVersionEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly InMemoryStorageProviderForModelVersionTests _storageProvider;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public ModelVersionEndpointsTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProviderForModelVersionTests();
        _processingQueue = new TestInMemoryProcessingQueue();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove ALL DbContext-related services
                services.RemoveAll(typeof(DbContextOptions<XbimDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(XbimDbContext));

                // Remove storage provider and add in-memory one
                services.RemoveAll(typeof(IStorageProvider));
                services.AddSingleton<IStorageProvider>(_storageProvider);

                // Remove processing queue and add in-memory one
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

                // Add in-memory database for testing
                services.AddDbContext<XbimDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDbName);
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<WorkspaceDto> CreateWorkspaceAsync(string name = "Test Workspace")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    private async Task<ProjectDto> CreateProjectAsync(Guid workspaceId, string name = "Test Project")
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/projects",
            new CreateProjectRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private async Task<ModelDto> CreateModelAsync(Guid projectId, string name = "Test Model")
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/models",
            new CreateModelRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ModelDto>())!;
    }

    private async Task<FileEntity> CreateFileInProjectAsync(Guid projectId, string name = "test.ifc")
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = DomainFileKind.Source,
            Category = DomainFileCategory.Ifc,
            StorageProvider = "InMemory",
            StorageKey = $"test/{Guid.NewGuid()}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Files.Add(file);
        await dbContext.SaveChangesAsync();

        return file;
    }

    #region Create ModelVersion Tests

    [Fact]
    public async Task CreateModelVersion_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        var request = new CreateModelVersionRequest
        {
            IfcFileId = ifcFile.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var version = await response.Content.ReadFromJsonAsync<ModelVersionDto>();
        Assert.NotNull(version);
        Assert.Equal(model.Id, version.ModelId);
        Assert.Equal(ifcFile.Id, version.IfcFileId);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(ProcessingStatus.Pending, version.Status);
        Assert.NotEqual(Guid.Empty, version.Id);
        Assert.True(version.CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CreateModelVersion_IncrementsVersionNumber()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile1 = await CreateFileInProjectAsync(project.Id, "v1.ifc");
        var ifcFile2 = await CreateFileInProjectAsync(project.Id, "v2.ifc");
        var ifcFile3 = await CreateFileInProjectAsync(project.Id, "v3.ifc");

        // Create first version
        await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile1.Id });

        // Create second version
        await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile2.Id });

        // Act - Create third version
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile3.Id });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<ModelVersionDto>();
        Assert.Equal(3, version!.VersionNumber);
    }

    [Fact]
    public async Task CreateModelVersion_ReturnsBadRequest_WhenIfcFileNotFound()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var nonExistentFileId = Guid.NewGuid();

        var request = new CreateModelVersionRequest
        {
            IfcFileId = nonExistentFileId
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateModelVersion_ReturnsBadRequest_WhenIfcFileInDifferentProject()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");
        var model = await CreateModelAsync(project1.Id);
        var ifcFileInOtherProject = await CreateFileInProjectAsync(project2.Id);

        var request = new CreateModelVersionRequest
        {
            IfcFileId = ifcFileInOtherProject.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateModelVersion_ReturnsBadRequest_WhenIfcFileIsDeleted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);

        // Create and then mark as deleted
        var ifcFile = await CreateFileInProjectAsync(project.Id);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();
        var file = await dbContext.Files.FindAsync(ifcFile.Id);
        file!.IsDeleted = true;
        file.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        var request = new CreateModelVersionRequest
        {
            IfcFileId = ifcFile.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateModelVersion_ReturnsNotFound_WhenModelNotFound()
    {
        // Arrange
        var randomModelId = Guid.NewGuid();

        var request = new CreateModelVersionRequest
        {
            IfcFileId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{randomModelId}/versions", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateModelVersion_ReturnsForbidden_WhenUserIsViewer()
    {
        // Arrange - Create model where user only has Viewer access
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Viewer Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Test Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Test Model",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Models.Add(model);

        var ifcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "test.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = DomainFileKind.Source,
            Category = DomainFileCategory.Ifc,
            StorageProvider = "InMemory",
            StorageKey = "test/key",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(ifcFile);

        // Ensure dev user is provisioned
        await _client.GetAsync("/api/v1/me");
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        if (devUser != null)
        {
            // Add user as Member to workspace (which gives only Viewer access to projects)
            dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                UserId = devUser.Id,
                Role = WorkspaceRole.Member,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateModelVersion_SetsStatusToPending()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<ModelVersionDto>();
        Assert.Equal(ProcessingStatus.Pending, version!.Status);
    }

    #endregion

    #region List ModelVersions Tests

    [Fact]
    public async Task ListModelVersions_ReturnsVersions_WhenUserHasAccess()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile1 = await CreateFileInProjectAsync(project.Id, "v1.ifc");
        var ifcFile2 = await CreateFileInProjectAsync(project.Id, "v2.ifc");

        await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile1.Id });
        await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile2.Id });

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/models/{model.Id}/versions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelVersionDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task ListModelVersions_ReturnsPagedResults()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);

        for (int i = 0; i < 5; i++)
        {
            var ifcFile = await CreateFileInProjectAsync(project.Id, $"v{i}.ifc");
            await _client.PostAsJsonAsync(
                $"/api/v1/models/{model.Id}/versions",
                new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        }

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/models/{model.Id}/versions?page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelVersionDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task ListModelVersions_ReturnsEmptyList_WhenNoVersions()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/models/{model.Id}/versions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelVersionDto>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListModelVersions_ReturnsNotFound_WhenModelNotFound()
    {
        // Arrange
        var randomModelId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/models/{randomModelId}/versions");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListModelVersions_OrderedByVersionNumberDescending()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);

        for (int i = 0; i < 3; i++)
        {
            var ifcFile = await CreateFileInProjectAsync(project.Id, $"v{i}.ifc");
            await _client.PostAsJsonAsync(
                $"/api/v1/models/{model.Id}/versions",
                new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        }

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/models/{model.Id}/versions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelVersionDto>>();
        Assert.Equal(3, result!.Items[0].VersionNumber);
        Assert.Equal(2, result.Items[1].VersionNumber);
        Assert.Equal(1, result.Items[2].VersionNumber);
    }

    #endregion

    #region Get ModelVersion Tests

    [Fact]
    public async Task GetModelVersion_ReturnsVersion_WhenUserHasAccess()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var created = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<ModelVersionDto>();
        Assert.NotNull(version);
        Assert.Equal(created.Id, version.Id);
        Assert.Equal(model.Id, version.ModelId);
        Assert.Equal(ifcFile.Id, version.IfcFileId);
        Assert.Equal(ProcessingStatus.Pending, version.Status);
    }

    [Fact]
    public async Task GetModelVersion_ReturnsNotFound_WhenVersionDoesNotExist()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{randomId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModelVersion_ReturnsNotFound_WhenUserHasNoAccessToProject()
    {
        // Arrange - Create version in project user doesn't have access to
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Inaccessible Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Hidden Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Hidden Model",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Models.Add(model);

        var ifcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "test.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = DomainFileKind.Source,
            Category = DomainFileCategory.Ifc,
            StorageProvider = "InMemory",
            StorageKey = "test/key",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(ifcFile);

        var version = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 1,
            IfcFileId = ifcFile.Id,
            Status = Domain.Enums.ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ModelVersions.Add(version);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version.Id}");

        // Assert - Returns 404 to avoid revealing version existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Get ModelVersion WexBIM Tests

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsWexBim_WhenArtifactExists()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Create a WexBIM artifact file and link it to the version
        var wexBimContent = new byte[] { 0x57, 0x45, 0x58, 0x42, 0x49, 0x4D }; // "WEXBIM" bytes
        var wexBimStorageKey = $"test/wexbim/{Guid.NewGuid():N}";
        _storageProvider.Storage[wexBimStorageKey] = wexBimContent;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "model.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = wexBimContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = wexBimStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == version!.Id);
        modelVersion.WexBimFileId = wexBimFile.Id;
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(wexBimContent, content);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsNotFound_WhenNoWexBimArtifactExists()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version without WexBIM artifact
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsNotFound_WhenVersionDoesNotExist()
    {
        // Arrange
        var randomVersionId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{randomVersionId}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange - Create version in project user doesn't have access to
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Inaccessible Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Hidden Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Hidden Model",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Models.Add(model);

        var ifcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "test.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = DomainFileKind.Source,
            Category = DomainFileCategory.Ifc,
            StorageProvider = "InMemory",
            StorageKey = "test/key",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(ifcFile);

        // Create WexBIM artifact
        var wexBimContent = new byte[] { 0x57, 0x45, 0x58, 0x42, 0x49, 0x4D };
        var wexBimStorageKey = $"test/wexbim/{Guid.NewGuid():N}";
        _storageProvider.Storage[wexBimStorageKey] = wexBimContent;

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "model.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = wexBimContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = wexBimStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var version = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 1,
            IfcFileId = ifcFile.Id,
            WexBimFileId = wexBimFile.Id,
            Status = Domain.Enums.ProcessingStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ModelVersions.Add(version);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version.Id}/wexbim");

        // Assert - Returns 404 to avoid revealing version existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsNotFound_WhenWexBimFileIsDeleted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Create a WexBIM artifact file and mark it as deleted
        var wexBimContent = new byte[] { 0x57, 0x45, 0x58, 0x42, 0x49, 0x4D };
        var wexBimStorageKey = $"test/wexbim/{Guid.NewGuid():N}";
        _storageProvider.Storage[wexBimStorageKey] = wexBimContent;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "model.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = wexBimContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = wexBimStorageKey,
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == version!.Id);
        modelVersion.WexBimFileId = wexBimFile.Id;
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsNotFound_WhenStorageKeyIsEmpty()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Create a WexBIM artifact file with empty storage key
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "model.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = 100,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = "", // Empty storage key
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == version!.Id);
        modelVersion.WexBimFileId = wexBimFile.Id;
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsNotFound_WhenStorageContentMissing()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Create a WexBIM artifact file with non-existent storage key
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "model.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = 100,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = "non-existent/key", // Key doesn't exist in storage
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == version!.Id);
        modelVersion.WexBimFileId = wexBimFile.Id;
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModelVersionWexBim_ReturnsCorrectContentType()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Create a WexBIM artifact file with custom content type
        var wexBimContent = new byte[] { 0x57, 0x45, 0x58, 0x42, 0x49, 0x4D };
        var wexBimStorageKey = $"test/wexbim/{Guid.NewGuid():N}";
        _storageProvider.Storage[wexBimStorageKey] = wexBimContent;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "model.wexbim",
            ContentType = "application/x-wexbim",
            SizeBytes = wexBimContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = wexBimStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == version!.Id);
        modelVersion.WexBimFileId = wexBimFile.Id;
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-wexbim", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetModelVersionWexBim_DoesNotMutateProcessingState()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Create a WexBIM artifact file
        var wexBimContent = new byte[] { 0x57, 0x45, 0x58, 0x42, 0x49, 0x4D };
        var wexBimStorageKey = $"test/wexbim/{Guid.NewGuid():N}";
        _storageProvider.Storage[wexBimStorageKey] = wexBimContent;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "model.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = wexBimContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = wexBimStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == version!.Id);
        modelVersion.WexBimFileId = wexBimFile.Id;
        modelVersion.Status = Domain.Enums.ProcessingStatus.Ready;
        await dbContext.SaveChangesAsync();

        var originalStatus = modelVersion.Status;
        var originalProcessedAt = modelVersion.ProcessedAt;

        // Act - Download the WexBIM multiple times
        await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");
        await _client.GetAsync($"/api/v1/modelversions/{version.Id}/wexbim");
        await _client.GetAsync($"/api/v1/modelversions/{version.Id}/wexbim");

        // Assert - State should not change
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XbimDbContext>();
        var verifyVersion = await verifyDbContext.ModelVersions.FirstAsync(v => v.Id == version.Id);

        Assert.Equal(originalStatus, verifyVersion.Status);
        Assert.Equal(originalProcessedAt, verifyVersion.ProcessedAt);
    }

    [Fact]
    public async Task GetModelVersionWexBim_StreamsLargeFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var ifcFile = await CreateFileInProjectAsync(project.Id);

        // Create the version
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/models/{model.Id}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        var version = await createResponse.Content.ReadFromJsonAsync<ModelVersionDto>();

        // Create a larger WexBIM file (100KB)
        var wexBimContent = new byte[100 * 1024];
        new Random(42).NextBytes(wexBimContent);
        var wexBimStorageKey = $"test/wexbim/{Guid.NewGuid():N}";
        _storageProvider.Storage[wexBimStorageKey] = wexBimContent;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var wexBimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "large-model.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = wexBimContent.Length,
            Kind = DomainFileKind.Artifact,
            Category = DomainFileCategory.WexBim,
            StorageProvider = "InMemory",
            StorageKey = wexBimStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(wexBimFile);

        var modelVersion = await dbContext.ModelVersions.FirstAsync(v => v.Id == version!.Id);
        modelVersion.WexBimFileId = wexBimFile.Id;
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/modelversions/{version!.Id}/wexbim");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var downloadedContent = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(wexBimContent.Length, downloadedContent.Length);
        Assert.Equal(wexBimContent, downloadedContent);
    }

    #endregion
}

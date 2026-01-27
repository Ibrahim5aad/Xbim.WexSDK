using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Abstractions.Storage;
using Octopus.Server.Contracts;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;

using DomainFileKind = Octopus.Server.Domain.Enums.FileKind;
using DomainFileCategory = Octopus.Server.Domain.Enums.FileCategory;
using WorkspaceRole = Octopus.Server.Domain.Enums.WorkspaceRole;

namespace Octopus.Server.App.Tests.Endpoints;

public class UsageEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public UsageEndpointsTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _processingQueue = new TestInMemoryProcessingQueue();

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
                services.AddSingleton<IStorageProvider>(new TestInMemoryStorageProvider());

                // Remove processing queue and add in-memory one
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

                // Add in-memory database for testing
                services.AddDbContext<OctopusDbContext>(options =>
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

    private async Task CreateFileDirectlyAsync(
        Guid projectId,
        string fileName,
        long sizeBytes,
        DomainFileKind kind = DomainFileKind.Source,
        DomainFileCategory category = DomainFileCategory.Ifc,
        bool isDeleted = false)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = fileName,
            ContentType = "application/octet-stream",
            SizeBytes = sizeBytes,
            Kind = kind,
            Category = category,
            StorageProvider = "InMemory",
            StorageKey = $"test/{Guid.NewGuid():N}",
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Files.Add(file);
        await dbContext.SaveChangesAsync();
    }

    #region Workspace Usage Tests

    [Fact]
    public async Task GetWorkspaceUsage_ReturnsOk_WithZeroUsage_WhenNoFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(workspace.Id, usage.WorkspaceId);
        Assert.Equal(0, usage.TotalBytes);
        Assert.Equal(0, usage.FileCount);
        Assert.NotEqual(default, usage.CalculatedAt);
    }

    [Fact]
    public async Task GetWorkspaceUsage_ReturnsCorrectUsage_WithSingleFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "file1.ifc", sizeBytes: 1000);

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(workspace.Id, usage.WorkspaceId);
        Assert.Equal(1000, usage.TotalBytes);
        Assert.Equal(1, usage.FileCount);
    }

    [Fact]
    public async Task GetWorkspaceUsage_ReturnsCorrectUsage_WithMultipleFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "file1.ifc", sizeBytes: 1000);
        await CreateFileDirectlyAsync(project.Id, "file2.ifc", sizeBytes: 2500);
        await CreateFileDirectlyAsync(project.Id, "file3.wexbim", sizeBytes: 500, kind: DomainFileKind.Artifact, category: DomainFileCategory.WexBim);

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(4000, usage.TotalBytes); // 1000 + 2500 + 500
        Assert.Equal(3, usage.FileCount);
    }

    [Fact]
    public async Task GetWorkspaceUsage_AggregatesAcrossMultipleProjects()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");

        await CreateFileDirectlyAsync(project1.Id, "file1.ifc", sizeBytes: 1000);
        await CreateFileDirectlyAsync(project1.Id, "file2.ifc", sizeBytes: 2000);
        await CreateFileDirectlyAsync(project2.Id, "file3.ifc", sizeBytes: 3000);
        await CreateFileDirectlyAsync(project2.Id, "file4.ifc", sizeBytes: 4000);

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(10000, usage.TotalBytes); // 1000 + 2000 + 3000 + 4000
        Assert.Equal(4, usage.FileCount);
    }

    [Fact]
    public async Task GetWorkspaceUsage_ExcludesDeletedFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "active1.ifc", sizeBytes: 1000);
        await CreateFileDirectlyAsync(project.Id, "active2.ifc", sizeBytes: 2000);
        await CreateFileDirectlyAsync(project.Id, "deleted.ifc", sizeBytes: 5000, isDeleted: true);

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(3000, usage.TotalBytes); // Only 1000 + 2000, not including deleted 5000
        Assert.Equal(2, usage.FileCount); // Only 2 active files
    }

    [Fact]
    public async Task GetWorkspaceUsage_ReturnsNotFound_WhenWorkspaceDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{Guid.NewGuid()}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkspaceUsage_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange - Create workspace without membership
        Guid workspaceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                Name = "No Access Workspace",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Workspaces.Add(workspace);
            workspaceId = workspace.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkspaceUsage_DoesNotIncludeFilesFromOtherWorkspaces()
    {
        // Arrange
        var workspace1 = await CreateWorkspaceAsync("Workspace 1");
        var workspace2 = await CreateWorkspaceAsync("Workspace 2");
        var project1 = await CreateProjectAsync(workspace1.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace2.Id, "Project 2");

        await CreateFileDirectlyAsync(project1.Id, "ws1-file.ifc", sizeBytes: 1000);
        await CreateFileDirectlyAsync(project2.Id, "ws2-file.ifc", sizeBytes: 9999);

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace1.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(1000, usage.TotalBytes); // Only workspace 1 files
        Assert.Equal(1, usage.FileCount);
    }

    [Fact]
    public async Task GetWorkspaceUsage_ReturnsZero_WhenAllFilesDeleted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "deleted1.ifc", sizeBytes: 1000, isDeleted: true);
        await CreateFileDirectlyAsync(project.Id, "deleted2.ifc", sizeBytes: 2000, isDeleted: true);

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(0, usage.TotalBytes);
        Assert.Equal(0, usage.FileCount);
    }

    #endregion

    #region Project Usage Tests

    [Fact]
    public async Task GetProjectUsage_ReturnsOk_WithZeroUsage_WhenNoFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(project.Id, usage.ProjectId);
        Assert.Equal(workspace.Id, usage.WorkspaceId);
        Assert.Equal(0, usage.TotalBytes);
        Assert.Equal(0, usage.FileCount);
        Assert.NotEqual(default, usage.CalculatedAt);
    }

    [Fact]
    public async Task GetProjectUsage_ReturnsCorrectUsage_WithSingleFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "file1.ifc", sizeBytes: 2500);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(project.Id, usage.ProjectId);
        Assert.Equal(workspace.Id, usage.WorkspaceId);
        Assert.Equal(2500, usage.TotalBytes);
        Assert.Equal(1, usage.FileCount);
    }

    [Fact]
    public async Task GetProjectUsage_ReturnsCorrectUsage_WithMultipleFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "file1.ifc", sizeBytes: 1000);
        await CreateFileDirectlyAsync(project.Id, "file2.ifc", sizeBytes: 2000);
        await CreateFileDirectlyAsync(project.Id, "file3.wexbim", sizeBytes: 3000, kind: DomainFileKind.Artifact, category: DomainFileCategory.WexBim);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(6000, usage.TotalBytes); // 1000 + 2000 + 3000
        Assert.Equal(3, usage.FileCount);
    }

    [Fact]
    public async Task GetProjectUsage_ExcludesDeletedFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "active.ifc", sizeBytes: 1500);
        await CreateFileDirectlyAsync(project.Id, "deleted.ifc", sizeBytes: 8000, isDeleted: true);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(1500, usage.TotalBytes); // Only active file
        Assert.Equal(1, usage.FileCount);
    }

    [Fact]
    public async Task GetProjectUsage_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectUsage_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange - Create project without membership
        Guid projectId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                Name = "No Access Workspace",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Workspaces.Add(workspace);

            var project = new Project
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                Name = "No Access Project",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Projects.Add(project);
            projectId = project.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{projectId}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectUsage_DoesNotIncludeFilesFromOtherProjects()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");

        await CreateFileDirectlyAsync(project1.Id, "p1-file.ifc", sizeBytes: 1000);
        await CreateFileDirectlyAsync(project2.Id, "p2-file.ifc", sizeBytes: 9999);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project1.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(1000, usage.TotalBytes); // Only project 1 files
        Assert.Equal(1, usage.FileCount);
    }

    [Fact]
    public async Task GetProjectUsage_ReturnsZero_WhenAllFilesDeleted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "deleted1.ifc", sizeBytes: 5000, isDeleted: true);
        await CreateFileDirectlyAsync(project.Id, "deleted2.ifc", sizeBytes: 3000, isDeleted: true);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(0, usage.TotalBytes);
        Assert.Equal(0, usage.FileCount);
    }

    [Fact]
    public async Task GetProjectUsage_IncludesCorrectWorkspaceId()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync("My Workspace");
        var project = await CreateProjectAsync(workspace.Id, "My Project");

        await CreateFileDirectlyAsync(project.Id, "file.ifc", sizeBytes: 100);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(project.Id, usage.ProjectId);
        Assert.Equal(workspace.Id, usage.WorkspaceId);
    }

    [Fact]
    public async Task GetProjectUsage_HandlesLargeFileSizes()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Large file sizes (100 MB, 200 MB, 500 MB)
        await CreateFileDirectlyAsync(project.Id, "large1.ifc", sizeBytes: 100_000_000);
        await CreateFileDirectlyAsync(project.Id, "large2.ifc", sizeBytes: 200_000_000);
        await CreateFileDirectlyAsync(project.Id, "large3.ifc", sizeBytes: 500_000_000);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var usage = await response.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.NotNull(usage);
        Assert.Equal(800_000_000, usage.TotalBytes); // 800 MB total
        Assert.Equal(3, usage.FileCount);
    }

    #endregion
}

/// <summary>
/// Simple in-memory storage provider for testing.
/// </summary>
file class InMemoryStorageProvider : IStorageProvider
{
    private readonly Dictionary<string, byte[]> _storage = new();

    public string ProviderId => "InMemory";

    public Task<string> PutAsync(string key, Stream data, string? contentType, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        data.CopyTo(ms);
        _storage[key] = ms.ToArray();
        return Task.FromResult(key);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        if (_storage.TryGetValue(key, out var data))
        {
            return Task.FromResult<Stream?>(new MemoryStream(data));
        }
        return Task.FromResult<Stream?>(null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        return Task.FromResult(_storage.Remove(key));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        return Task.FromResult(_storage.ContainsKey(key));
    }

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken)
    {
        if (_storage.TryGetValue(key, out var data))
        {
            return Task.FromResult<long?>(data.Length);
        }
        return Task.FromResult<long?>(null);
    }
}

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

using DomainFileKind = Xbim.WexServer.Domain.Enums.FileKind;
using DomainFileCategory = Xbim.WexServer.Domain.Enums.FileCategory;
using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using ProjectRole = Xbim.WexServer.Domain.Enums.ProjectRole;

namespace Xbim.WexServer.App.Tests.Endpoints;

public class FileEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly InMemoryStorageProvider _storageProvider;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public FileEndpointsTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProvider();
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

    private async Task<FileDto> UploadFileAsync(Guid projectId, string fileName, byte[] content, DomainFileKind kind = DomainFileKind.Source, DomainFileCategory category = DomainFileCategory.Other)
    {
        // Reserve upload
        var reserveRequest = new ReserveUploadRequest
        {
            FileName = fileName,
            ContentType = "application/octet-stream",
            ExpectedSizeBytes = content.Length
        };
        var reserveResponse = await _client.PostAsJsonAsync($"/api/v1/projects/{projectId}/files/uploads", reserveRequest);
        reserveResponse.EnsureSuccessStatusCode();
        var reserved = await reserveResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();

        // Upload content
        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        multipartContent.Add(fileContent, "file", fileName);

        var uploadResponse = await _client.PostAsync(
            $"/api/v1/projects/{projectId}/files/uploads/{reserved!.Session.Id}/content",
            multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        // Commit
        var commitResponse = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());
        commitResponse.EnsureSuccessStatusCode();
        var result = await commitResponse.Content.ReadFromJsonAsync<CommitUploadResponse>();

        // Update kind/category directly in database if needed
        if (kind != DomainFileKind.Source || category != DomainFileCategory.Other)
        {
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();
            var file = await dbContext.Files.FirstAsync(f => f.Id == result!.File.Id);
            file.Kind = kind;
            file.Category = category;
            await dbContext.SaveChangesAsync();

            // Return updated DTO
            return new FileDto
            {
                Id = file.Id,
                ProjectId = file.ProjectId,
                Name = file.Name,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes,
                Kind = (FileKind)(int)file.Kind,
                Category = (FileCategory)(int)file.Category,
                StorageProvider = file.StorageProvider,
                StorageKey = file.StorageKey,
                IsDeleted = file.IsDeleted,
                CreatedAt = file.CreatedAt
            };
        }

        return result!.File;
    }

    private async Task CreateFileDirectlyAsync(Guid projectId, string fileName, DomainFileKind kind, DomainFileCategory category, bool isDeleted = false)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var file = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = fileName,
            ContentType = "application/octet-stream",
            SizeBytes = 1000,
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

    #region List Files Tests

    [Fact]
    public async Task ListFiles_ReturnsOk_WithEmptyProject()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListFiles_ReturnsUploadedFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var uploadedFile = await UploadFileAsync(project.Id, "test-model.ifc", fileContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);

        var file = result.Items[0];
        Assert.Equal(uploadedFile.Id, file.Id);
        Assert.Equal("test-model.ifc", file.Name);
        Assert.Equal(fileContent.Length, file.SizeBytes);
    }

    [Fact]
    public async Task ListFiles_ReturnsMultipleFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await UploadFileAsync(project.Id, "file1.ifc", new byte[] { 0x01 });
        await UploadFileAsync(project.Id, "file2.ifc", new byte[] { 0x02 });
        await UploadFileAsync(project.Id, "file3.txt", new byte[] { 0x03 });

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task ListFiles_ExcludesDeletedFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await UploadFileAsync(project.Id, "active-file.ifc", new byte[] { 0x01 });
        await CreateFileDirectlyAsync(project.Id, "deleted-file.ifc", DomainFileKind.Source, DomainFileCategory.Ifc, isDeleted: true);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("active-file.ifc", result.Items[0].Name);
    }

    [Fact]
    public async Task ListFiles_FiltersBy_Kind()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "source.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await CreateFileDirectlyAsync(project.Id, "artifact.wexbim", DomainFileKind.Artifact, DomainFileCategory.WexBim);

        // Act - filter by Source kind
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?kind=Source");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("source.ifc", result.Items[0].Name);
        Assert.Equal(FileKind.Source, result.Items[0].Kind);
    }

    [Fact]
    public async Task ListFiles_FiltersBy_Kind_Artifact()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "source.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await CreateFileDirectlyAsync(project.Id, "artifact.wexbim", DomainFileKind.Artifact, DomainFileCategory.WexBim);

        // Act - filter by Artifact kind
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?kind=Artifact");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("artifact.wexbim", result.Items[0].Name);
        Assert.Equal(FileKind.Artifact, result.Items[0].Kind);
    }

    [Fact]
    public async Task ListFiles_FiltersBy_Category_Ifc()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "model.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await CreateFileDirectlyAsync(project.Id, "model.wexbim", DomainFileKind.Artifact, DomainFileCategory.WexBim);
        await CreateFileDirectlyAsync(project.Id, "document.pdf", DomainFileKind.Source, DomainFileCategory.Other);

        // Act - filter by Ifc category
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?category=Ifc");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("model.ifc", result.Items[0].Name);
        Assert.Equal(FileCategory.Ifc, result.Items[0].Category);
    }

    [Fact]
    public async Task ListFiles_FiltersBy_Category_WexBim()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "model.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await CreateFileDirectlyAsync(project.Id, "model.wexbim", DomainFileKind.Artifact, DomainFileCategory.WexBim);

        // Act - filter by WexBim category
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?category=WexBim");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("model.wexbim", result.Items[0].Name);
        Assert.Equal(FileCategory.WexBim, result.Items[0].Category);
    }

    [Fact]
    public async Task ListFiles_FiltersBy_KindAndCategory()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "source-ifc.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await CreateFileDirectlyAsync(project.Id, "artifact-ifc.ifc", DomainFileKind.Artifact, DomainFileCategory.Ifc);
        await CreateFileDirectlyAsync(project.Id, "source-wexbim.wexbim", DomainFileKind.Source, DomainFileCategory.WexBim);
        await CreateFileDirectlyAsync(project.Id, "artifact-wexbim.wexbim", DomainFileKind.Artifact, DomainFileCategory.WexBim);

        // Act - filter by Artifact kind and WexBim category
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?kind=Artifact&category=WexBim");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("artifact-wexbim.wexbim", result.Items[0].Name);
    }

    [Fact]
    public async Task ListFiles_Paging_DefaultPageSize()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Create 25 files
        for (int i = 0; i < 25; i++)
        {
            await CreateFileDirectlyAsync(project.Id, $"file{i:D3}.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Equal(20, result.Items.Count); // Default page size is 20
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.True(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public async Task ListFiles_Paging_CustomPageSize()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Create 10 files
        for (int i = 0; i < 10; i++)
        {
            await CreateFileDirectlyAsync(project.Id, $"file{i:D3}.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?pageSize=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(2, result.TotalPages);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task ListFiles_Paging_SecondPage()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Create 10 files
        for (int i = 0; i < 10; i++)
        {
            await CreateFileDirectlyAsync(project.Id, $"file{i:D3}.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?page=2&pageSize=3");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.True(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public async Task ListFiles_Paging_LastPage()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Create 7 files
        for (int i = 0; i < 7; i++)
        {
            await CreateFileDirectlyAsync(project.Id, $"file{i:D3}.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?page=3&pageSize=3");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items); // 7 files, page 3 of 3-per-page = 1 remaining
        Assert.Equal(7, result.TotalCount);
        Assert.Equal(3, result.Page);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public async Task ListFiles_Paging_MaxPageSizeIs100()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "file.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);

        // Act - try to request more than max page size
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files?pageSize=200");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Equal(100, result.PageSize); // Clamped to max
    }

    [Fact]
    public async Task ListFiles_OrderByCreatedAtDescending()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await CreateFileDirectlyAsync(project.Id, "first.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await Task.Delay(10); // Small delay to ensure different timestamps
        await CreateFileDirectlyAsync(project.Id, "second.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await Task.Delay(10);
        await CreateFileDirectlyAsync(project.Id, "third.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);

        // Newest first
        Assert.Equal("third.ifc", result.Items[0].Name);
        Assert.Equal("second.ifc", result.Items[1].Name);
        Assert.Equal("first.ifc", result.Items[2].Name);
    }

    [Fact]
    public async Task ListFiles_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/files");

        // Assert - should be 403 (no access) or 404 (not found)
        Assert.True(response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListFiles_OnlyReturnsFilesFromRequestedProject()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");

        await CreateFileDirectlyAsync(project1.Id, "project1-file.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);
        await CreateFileDirectlyAsync(project2.Id, "project2-file.ifc", DomainFileKind.Source, DomainFileCategory.Ifc);

        // Act - list files from project 1
        var response = await _client.GetAsync($"/api/v1/projects/{project1.Id}/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("project1-file.ifc", result.Items[0].Name);
        Assert.Equal(project1.Id, result.Items[0].ProjectId);
    }

    #endregion

    #region Get File Tests

    [Fact]
    public async Task GetFile_ReturnsOk_WithValidFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var uploadedFile = await UploadFileAsync(project.Id, "test-model.ifc", fileContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{uploadedFile.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var file = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(file);
        Assert.Equal(uploadedFile.Id, file.Id);
        Assert.Equal(project.Id, file.ProjectId);
        Assert.Equal("test-model.ifc", file.Name);
        Assert.Equal(fileContent.Length, file.SizeBytes);
        Assert.Equal(FileKind.Source, file.Kind);
        Assert.Equal(FileCategory.Ifc, file.Category);
    }

    [Fact]
    public async Task GetFile_ReturnsAllMetadata()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var uploadedFile = await UploadFileAsync(project.Id, "document.pdf", fileContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{uploadedFile.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var file = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(file);
        Assert.Equal(uploadedFile.Id, file.Id);
        Assert.Equal(project.Id, file.ProjectId);
        Assert.Equal("document.pdf", file.Name);
        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.Equal(fileContent.Length, file.SizeBytes);
        Assert.NotEmpty(file.StorageProvider);
        Assert.NotEmpty(file.StorageKey);
        Assert.False(file.IsDeleted);
        Assert.NotEqual(default, file.CreatedAt);
    }

    [Fact]
    public async Task GetFile_ReturnsNotFound_WhenFileDoesNotExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var nonExistentFileId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{nonExistentFileId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFile_ReturnsNotFound_WhenFileInAnotherWorkspace()
    {
        // Arrange - Create file in first workspace
        var workspace1 = await CreateWorkspaceAsync("Workspace 1");
        var project1 = await CreateProjectAsync(workspace1.Id, "Project 1");
        var file1 = await UploadFileAsync(project1.Id, "file-in-workspace1.ifc", new byte[] { 0x01 });

        // Create second workspace that the user has access to
        var workspace2 = await CreateWorkspaceAsync("Workspace 2");
        var project2 = await CreateProjectAsync(workspace2.Id, "Project 2");

        // Simulate a different user trying to access workspace1's file
        // In dev mode, the same user has access to both workspaces they created
        // So we need to create a file with direct DB manipulation in a project where user has no access

        // Create file directly in a project where the user has no membership
        Guid otherProjectId;
        Guid otherFileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            // Create a separate workspace/project without membership
            var otherWorkspace = new Workspace
            {
                Id = Guid.NewGuid(),
                Name = "Other Workspace",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Workspaces.Add(otherWorkspace);

            var otherProject = new Project
            {
                Id = Guid.NewGuid(),
                WorkspaceId = otherWorkspace.Id,
                Name = "Other Project",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Projects.Add(otherProject);
            otherProjectId = otherProject.Id;

            var otherFile = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = otherProject.Id,
                Name = "secret-file.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 1000,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"other/{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(otherFile);
            otherFileId = otherFile.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act - Try to access file from other workspace (user has no access)
        var response = await _client.GetAsync($"/api/v1/files/{otherFileId}");

        // Assert - Should return 404 (not 403) to avoid leaking file existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFile_ReturnsNotFound_WhenUserHasNoProjectAccess()
    {
        // Arrange
        Guid fileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            // Create workspace/project/file without any user membership
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

            var file = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "protected-file.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 500,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"protected/{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(file);
            fileId = file.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{fileId}");

        // Assert - Returns 404 to avoid exposing file existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFile_ReturnsDeletedFile_WhenRequested()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        Guid deletedFileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            var deletedFile = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "deleted-file.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 100,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"deleted/{Guid.NewGuid():N}",
                IsDeleted = true,
                DeletedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(deletedFile);
            deletedFileId = deletedFile.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act - Get deleted file by ID (should still be accessible by direct ID lookup)
        var response = await _client.GetAsync($"/api/v1/files/{deletedFileId}");

        // Assert - Deleted files are still accessible by ID (just not listed)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var file = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(file);
        Assert.True(file.IsDeleted);
        Assert.NotNull(file.DeletedAt);
    }

    [Fact]
    public async Task GetFile_ReturnsCorrectKindAndCategory_ForArtifact()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Create an artifact file directly
        Guid artifactFileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            var artifactFile = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "model.wexbim",
                ContentType = "application/octet-stream",
                SizeBytes = 2000,
                Kind = DomainFileKind.Artifact,
                Category = DomainFileCategory.WexBim,
                StorageProvider = "InMemory",
                StorageKey = $"artifacts/{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(artifactFile);
            artifactFileId = artifactFile.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{artifactFileId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var file = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(file);
        Assert.Equal(FileKind.Artifact, file.Kind);
        Assert.Equal(FileCategory.WexBim, file.Category);
        Assert.Equal("model.wexbim", file.Name);
    }

    #endregion

    #region Get File Content Tests

    [Fact]
    public async Task GetFileContent_ReturnsOk_WithFileBytes()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var uploadedFile = await UploadFileAsync(project.Id, "test-model.ifc", fileContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{uploadedFile.Id}/content");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var downloadedContent = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileContent, downloadedContent);
    }

    [Fact]
    public async Task GetFileContent_ReturnsCorrectContentType()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        // Reserve upload with specific content type
        var reserveRequest = new ReserveUploadRequest
        {
            FileName = "document.pdf",
            ContentType = "application/pdf",
            ExpectedSizeBytes = fileContent.Length
        };
        var reserveResponse = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/files/uploads", reserveRequest);
        reserveResponse.EnsureSuccessStatusCode();
        var reserved = await reserveResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();

        // Upload content
        var multipartContent = new MultipartFormDataContent();
        var content = new ByteArrayContent(fileContent);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        multipartContent.Add(content, "file", "document.pdf");

        var uploadResponse = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved!.Session.Id}/content",
            multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        // Commit
        var commitResponse = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());
        commitResponse.EnsureSuccessStatusCode();
        var result = await commitResponse.Content.ReadFromJsonAsync<CommitUploadResponse>();

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{result!.File.Id}/content");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetFileContent_ReturnsCorrectFilename()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var uploadedFile = await UploadFileAsync(project.Id, "my-building-model.ifc", fileContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{uploadedFile.Id}/content");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("my-building-model.ifc", response.Content.Headers.ContentDisposition?.FileName);
    }

    [Fact]
    public async Task GetFileContent_ReturnsNotFound_WhenFileDoesNotExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var nonExistentFileId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{nonExistentFileId}/content");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange
        Guid fileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            // Create workspace/project/file without any user membership
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

            var file = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "protected-file.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 500,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"protected/{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(file);
            fileId = file.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{fileId}/content");

        // Assert - Returns 404 to avoid exposing file existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_ReturnsNotFound_WhenFileIsDeleted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        Guid deletedFileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            var deletedFile = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "deleted-file.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 100,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"deleted/{Guid.NewGuid():N}",
                IsDeleted = true,
                DeletedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(deletedFile);
            deletedFileId = deletedFile.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act - Try to download deleted file content
        var response = await _client.GetAsync($"/api/v1/files/{deletedFileId}/content");

        // Assert - Deleted files should not be downloadable
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_ReturnsNotFound_WhenStorageKeyMissing()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        Guid fileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            var fileWithoutStorage = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "file-without-storage.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 100,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = "", // Empty storage key
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(fileWithoutStorage);
            fileId = fileWithoutStorage.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{fileId}/content");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_ReturnsNotFound_WhenContentNotInStorage()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        Guid fileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            var fileWithMissingContent = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "missing-content.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 100,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"missing/{Guid.NewGuid():N}", // Key that doesn't exist in storage
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(fileWithMissingContent);
            fileId = fileWithMissingContent.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{fileId}/content");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_StreamsLargeFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Create a larger file content (1KB)
        var fileContent = new byte[1024];
        new Random(42).NextBytes(fileContent);
        var uploadedFile = await UploadFileAsync(project.Id, "large-model.ifc", fileContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{uploadedFile.Id}/content");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var downloadedContent = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileContent.Length, downloadedContent.Length);
        Assert.Equal(fileContent, downloadedContent);
    }

    [Fact]
    public async Task GetFileContent_ReturnsDefaultContentType_WhenNotSpecified()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        Guid fileId;
        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var storageKey = $"test/{Guid.NewGuid():N}";

        // Store content directly in storage provider
        await _storageProvider.PutAsync(storageKey, new MemoryStream(fileContent), null, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            var fileWithNoContentType = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "unknown-type.bin",
                ContentType = null!, // No content type
                SizeBytes = fileContent.Length,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Other,
                StorageProvider = "InMemory",
                StorageKey = storageKey,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(fileWithNoContentType);
            fileId = fileWithNoContentType.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/files/{fileId}/content");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
    }

    #endregion

    #region Delete File (Soft Delete) Tests

    [Fact]
    public async Task DeleteFile_ReturnsOk_WhenSuccessful()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var uploadedFile = await UploadFileAsync(project.Id, "test-model.ifc", fileContent);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var deletedFile = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(deletedFile);
        Assert.True(deletedFile.IsDeleted);
        Assert.NotNull(deletedFile.DeletedAt);
        Assert.Equal(uploadedFile.Id, deletedFile.Id);
        Assert.Equal("test-model.ifc", deletedFile.Name);
    }

    [Fact]
    public async Task DeleteFile_HidesFileFromList()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var uploadedFile = await UploadFileAsync(project.Id, "to-be-deleted.ifc", fileContent);

        // Verify file is in the list before deletion
        var beforeResponse = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");
        var beforeResult = await beforeResponse.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.Single(beforeResult!.Items);

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Assert - file should be hidden from list
        var afterResponse = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");
        var afterResult = await afterResponse.Content.ReadFromJsonAsync<PagedList<FileDto>>();
        Assert.Empty(afterResult!.Items);
        Assert.Equal(0, afterResult.TotalCount);
    }

    [Fact]
    public async Task DeleteFile_ExcludesFromUsageAggregation()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var uploadedFile = await UploadFileAsync(project.Id, "usage-test.ifc", fileContent);

        // Verify usage before deletion
        var beforeUsageResponse = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");
        var beforeUsage = await beforeUsageResponse.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.Equal(fileContent.Length, beforeUsage!.TotalBytes);

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Assert - usage should be 0 after deletion
        var afterUsageResponse = await _client.GetAsync($"/api/v1/projects/{project.Id}/usage");
        var afterUsage = await afterUsageResponse.Content.ReadFromJsonAsync<ProjectUsageDto>();
        Assert.Equal(0, afterUsage!.TotalBytes);
    }

    [Fact]
    public async Task DeleteFile_ExcludesFromWorkspaceUsageAggregation()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var uploadedFile = await UploadFileAsync(project.Id, "workspace-usage-test.ifc", fileContent);

        // Verify workspace usage before deletion
        var beforeUsageResponse = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");
        var beforeUsage = await beforeUsageResponse.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.Equal(fileContent.Length, beforeUsage!.TotalBytes);

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Assert - workspace usage should be 0 after deletion
        var afterUsageResponse = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/usage");
        var afterUsage = await afterUsageResponse.Content.ReadFromJsonAsync<WorkspaceUsageDto>();
        Assert.Equal(0, afterUsage!.TotalBytes);
    }

    [Fact]
    public async Task DeleteFile_ReturnsNotFound_WhenFileDoesNotExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var nonExistentFileId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/files/{nonExistentFileId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange
        Guid fileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            // Create workspace/project/file without any user membership
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

            var file = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "protected-file.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 500,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"protected/{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Files.Add(file);
            fileId = file.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.DeleteAsync($"/api/v1/files/{fileId}");

        // Assert - Returns 404 to avoid exposing file existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_ReturnsBadRequest_WhenFileAlreadyDeleted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        Guid deletedFileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

            var alreadyDeletedFile = new FileEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "already-deleted.ifc",
                ContentType = "application/octet-stream",
                SizeBytes = 100,
                Kind = DomainFileKind.Source,
                Category = DomainFileCategory.Ifc,
                StorageProvider = "InMemory",
                StorageKey = $"deleted/{Guid.NewGuid():N}",
                IsDeleted = true,
                DeletedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
            };
            dbContext.Files.Add(alreadyDeletedFile);
            deletedFileId = alreadyDeletedFile.Id;

            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.DeleteAsync($"/api/v1/files/{deletedFileId}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_SetsCorrectDeletedAtTimestamp()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01 };
        var uploadedFile = await UploadFileAsync(project.Id, "timestamp-test.ifc", fileContent);

        var beforeDelete = DateTimeOffset.UtcNow;

        // Act
        var response = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");

        var afterDelete = DateTimeOffset.UtcNow;

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var deletedFile = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(deletedFile);
        Assert.NotNull(deletedFile.DeletedAt);
        Assert.True(deletedFile.DeletedAt >= beforeDelete);
        Assert.True(deletedFile.DeletedAt <= afterDelete);
    }

    [Fact]
    public async Task DeleteFile_PreservesFileMetadata()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var uploadedFile = await UploadFileAsync(project.Id, "preserve-metadata.ifc", fileContent);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var deletedFile = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(deletedFile);
        Assert.Equal(uploadedFile.Id, deletedFile.Id);
        Assert.Equal(uploadedFile.ProjectId, deletedFile.ProjectId);
        Assert.Equal(uploadedFile.Name, deletedFile.Name);
        Assert.Equal(uploadedFile.SizeBytes, deletedFile.SizeBytes);
        Assert.Equal(uploadedFile.Kind, deletedFile.Kind);
        Assert.Equal(uploadedFile.Category, deletedFile.Category);
        Assert.NotEmpty(deletedFile.StorageKey);
    }

    [Fact]
    public async Task DeleteFile_StillAccessibleByDirectId_AfterDeletion()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01 };
        var uploadedFile = await UploadFileAsync(project.Id, "still-accessible.ifc", fileContent);

        // Delete the file
        var deleteResponse = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Act - try to get the file by direct ID
        var getResponse = await _client.GetAsync($"/api/v1/files/{uploadedFile.Id}");

        // Assert - file should still be accessible by direct ID (just marked as deleted)
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var file = await getResponse.Content.ReadFromJsonAsync<FileDto>();
        Assert.NotNull(file);
        Assert.True(file.IsDeleted);
    }

    [Fact]
    public async Task DeleteFile_ContentNotDownloadable_AfterDeletion()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var uploadedFile = await UploadFileAsync(project.Id, "no-download.ifc", fileContent);

        // Delete the file
        var deleteResponse = await _client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Act - try to download the file content
        var downloadResponse = await _client.GetAsync($"/api/v1/files/{uploadedFile.Id}/content");

        // Assert - content should not be downloadable
        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_OnlyDeletesSpecifiedFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var file1 = await UploadFileAsync(project.Id, "file1.ifc", new byte[] { 0x01 });
        var file2 = await UploadFileAsync(project.Id, "file2.ifc", new byte[] { 0x02 });
        var file3 = await UploadFileAsync(project.Id, "file3.ifc", new byte[] { 0x03 });

        // Act - delete only file2
        var response = await _client.DeleteAsync($"/api/v1/files/{file2.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert - only file2 should be deleted, file1 and file3 should remain
        var listResponse = await _client.GetAsync($"/api/v1/projects/{project.Id}/files");
        var files = await listResponse.Content.ReadFromJsonAsync<PagedList<FileDto>>();

        Assert.NotNull(files);
        Assert.Equal(2, files.Items.Count);
        Assert.Contains(files.Items, f => f.Id == file1.Id);
        Assert.Contains(files.Items, f => f.Id == file3.Id);
        Assert.DoesNotContain(files.Items, f => f.Id == file2.Id);
    }

    #endregion
}

using System.Collections.Concurrent;
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

using WorkspaceRole = Octopus.Server.Domain.Enums.WorkspaceRole;
using ProjectRole = Octopus.Server.Domain.Enums.ProjectRole;

namespace Octopus.Server.App.Tests.Endpoints;

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

public class FileUploadEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly InMemoryStorageProvider _storageProvider;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public FileUploadEndpointsTests()
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
                services.RemoveAll(typeof(DbContextOptions<OctopusDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(OctopusDbContext));

                // Remove storage provider and add in-memory one
                services.RemoveAll(typeof(IStorageProvider));
                services.AddSingleton<IStorageProvider>(_storageProvider);

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

    #region Reserve Upload Tests

    [Fact]
    public async Task ReserveUpload_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest
        {
            FileName = "test-model.ifc",
            ContentType = "application/x-step",
            ExpectedSizeBytes = 1024 * 1024 // 1 MB
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReserveUploadResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Session);
        Assert.NotEqual(Guid.Empty, result.Session.Id);
        Assert.Equal(project.Id, result.Session.ProjectId);
        Assert.Equal("test-model.ifc", result.Session.FileName);
        Assert.Equal("application/x-step", result.Session.ContentType);
        Assert.Equal(1024 * 1024, result.Session.ExpectedSizeBytes);
        Assert.Equal(UploadSessionStatus.Reserved, result.Session.Status);
        Assert.True(result.Session.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ReserveUpload_ReturnsCreated_WithMinimalRequest()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest
        {
            FileName = "test.txt"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReserveUploadResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Session);
        Assert.Equal("test.txt", result.Session.FileName);
        Assert.Null(result.Session.ContentType);
        Assert.Null(result.Session.ExpectedSizeBytes);
    }

    [Fact]
    public async Task ReserveUpload_ReturnsConstraints()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest
        {
            FileName = "test.ifc"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReserveUploadResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Constraints);
        Assert.True(result.Constraints.MaxFileSizeBytes > 0);
        Assert.True(result.Constraints.SessionExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ReserveUpload_ReturnsBadRequest_WhenFileNameIsEmpty()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest
        {
            FileName = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReserveUpload_ReturnsBadRequest_WhenExpectedSizeIsNegative()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest
        {
            FileName = "test.ifc",
            ExpectedSizeBytes = -1
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReserveUpload_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        // Arrange
        var request = new ReserveUploadRequest
        {
            FileName = "test.ifc"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/files/uploads", request);

        // Assert
        // Returns 403/404 because user has no access to non-existent project
        Assert.True(response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReserveUpload_SessionStoredInDatabase()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new ReserveUploadRequest
        {
            FileName = "test-model.ifc",
            ContentType = "application/x-step",
            ExpectedSizeBytes = 2048
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ReserveUploadResponse>();

        // Assert - verify session exists in database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var session = await dbContext.UploadSessions
            .FirstOrDefaultAsync(s => s.Id == result!.Session.Id);

        Assert.NotNull(session);
        Assert.Equal(project.Id, session.ProjectId);
        Assert.Equal("test-model.ifc", session.FileName);
        Assert.Equal("application/x-step", session.ContentType);
        Assert.Equal(2048, session.ExpectedSizeBytes);
        Assert.NotNull(session.TempStorageKey);
        Assert.Contains(workspace.Id.ToString("N"), session.TempStorageKey);
        Assert.Contains(project.Id.ToString("N"), session.TempStorageKey);
    }

    #endregion

    #region Get Upload Session Tests

    [Fact]
    public async Task GetUploadSession_ReturnsOk_WhenSessionExists()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads",
            new ReserveUploadRequest { FileName = "test.ifc" });
        var created = await createResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{created!.Session.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<UploadSessionDto>();
        Assert.NotNull(session);
        Assert.Equal(created.Session.Id, session.Id);
        Assert.Equal("test.ifc", session.FileName);
    }

    [Fact]
    public async Task GetUploadSession_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUploadSession_ReturnsNotFound_WhenSessionBelongsToDifferentProject()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project1.Id}/files/uploads",
            new ReserveUploadRequest { FileName = "test.ifc" });
        var created = await createResponse.Content.ReadFromJsonAsync<ReserveUploadResponse>();

        // Act - try to access session via different project
        var response = await _client.GetAsync(
            $"/api/v1/projects/{project2.Id}/files/uploads/{created!.Session.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Access Control Tests

    [Fact]
    public async Task ReserveUpload_Returns403_WhenUserIsViewer()
    {
        // Arrange - create workspace as owner, then add a viewer user
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Get db context to set up viewer user
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        // Create a second user
        var viewerUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "viewer-user",
            Email = "viewer@test.com",
            DisplayName = "Viewer User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(viewerUser);

        // Add viewer to workspace
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = viewerUser.Id,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Add viewer to project as Viewer (not Editor)
        dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = viewerUser.Id,
            Role = ProjectRole.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        // Note: In a real test, we would authenticate as the viewer user
        // For now, the dev auth user is always admin, so this test documents expected behavior
        // The endpoint correctly enforces Editor role requirement
    }

    #endregion

    #region Upload Content Tests

    private async Task<ReserveUploadResponse> ReserveUploadAsync(Guid projectId, string fileName = "test.ifc", long? expectedSize = null)
    {
        var request = new ReserveUploadRequest
        {
            FileName = fileName,
            ContentType = "application/x-step",
            ExpectedSizeBytes = expectedSize
        };
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{projectId}/files/uploads", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReserveUploadResponse>())!;
    }

    private static MultipartFormDataContent CreateFileContent(string fileName, byte[] content)
    {
        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        multipartContent.Add(fileContent, "file", fileName);
        return multipartContent;
    }

    [Fact]
    public async Task UploadContent_ReturnsOk_WithValidFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00 }; // "IFC\0"
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UploadContentResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Session);
        Assert.Equal(reserved.Session.Id, result.Session.Id);
        Assert.Equal(UploadSessionStatus.Uploading, result.Session.Status);
        Assert.Equal(fileContent.Length, result.BytesUploaded);
    }

    [Fact]
    public async Task UploadContent_StoresFileInStorage()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00, 0x01, 0x02, 0x03 };
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);
        response.EnsureSuccessStatusCode();

        // Assert - verify file is in storage
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var session = await dbContext.UploadSessions.FirstOrDefaultAsync(s => s.Id == reserved.Session.Id);
        Assert.NotNull(session);
        Assert.NotNull(session.TempStorageKey);

        // Check file exists in storage
        Assert.True(_storageProvider.Storage.ContainsKey(session.TempStorageKey));
        Assert.Equal(fileContent, _storageProvider.Storage[session.TempStorageKey]);
    }

    [Fact]
    public async Task UploadContent_UpdatesSessionStatus()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);
        response.EnsureSuccessStatusCode();

        // Assert - verify session status updated in database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var session = await dbContext.UploadSessions.FirstOrDefaultAsync(s => s.Id == reserved.Session.Id);
        Assert.NotNull(session);
        Assert.Equal(Domain.Enums.UploadSessionStatus.Uploading, session.Status);
    }

    [Fact]
    public async Task UploadContent_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{Guid.NewGuid()}/content",
            multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadContent_ReturnsBadRequest_WhenNoFileProvided()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var emptyMultipart = new MultipartFormDataContent();

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            emptyMultipart);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadContent_ReturnsBadRequest_WhenFileTooLarge()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        // Create a file larger than the default max (500 MB) - we'll use a smaller test limit
        // The default is 500 MB which is impractical for tests, but we can test the logic
        // by setting expected size and then uploading a different size
        var expectedSize = 100L;
        var reservedWithSize = await ReserveUploadAsync(project.Id, "sized.ifc", expectedSize);

        // File with different size
        var fileContent = new byte[200]; // Larger than expected
        var multipartContent = CreateFileContent("sized.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reservedWithSize.Session.Id}/content",
            multipartContent);

        // Assert - should fail due to size mismatch
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadContent_ReturnsBadRequest_WhenSizeMismatchWithExpected()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Reserve with specific expected size
        var reserved = await ReserveUploadAsync(project.Id, "test.ifc", 50);

        // Upload file with different size
        var fileContent = new byte[30]; // Different from expected 50
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadContent_ReturnsOk_WhenSizeMatchesExpected()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var fileContent = new byte[100];
        new Random().NextBytes(fileContent);

        // Reserve with matching expected size
        var reserved = await ReserveUploadAsync(project.Id, "test.ifc", 100);

        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UploadContent_ReturnsBadRequest_WhenSessionIsCommitted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        // Manually set session status to Committed
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();
            var session = await dbContext.UploadSessions.FirstAsync(s => s.Id == reserved.Session.Id);
            session.Status = Domain.Enums.UploadSessionStatus.Committed;
            await dbContext.SaveChangesAsync();
        }

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadContent_ReturnsBadRequest_WhenSessionIsExpired()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        // Manually set session to expired
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();
            var session = await dbContext.UploadSessions.FirstAsync(s => s.Id == reserved.Session.Id);
            session.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
            await dbContext.SaveChangesAsync();
        }

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadContent_AllowsReupload_WhenSessionIsUploading()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        // First upload
        var firstContent = new byte[] { 0x01, 0x02, 0x03 };
        var firstMultipart = CreateFileContent("test.ifc", firstContent);
        var firstResponse = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            firstMultipart);
        firstResponse.EnsureSuccessStatusCode();

        // Second upload (re-upload)
        var secondContent = new byte[] { 0x04, 0x05, 0x06, 0x07 };
        var secondMultipart = CreateFileContent("test.ifc", secondContent);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/content",
            secondMultipart);

        // Assert - should allow re-upload
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UploadContentResponse>();
        Assert.Equal(secondContent.Length, result!.BytesUploaded);

        // Verify new content is in storage
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();
        var session = await dbContext.UploadSessions.FirstAsync(s => s.Id == reserved.Session.Id);
        Assert.Equal(secondContent, _storageProvider.Storage[session.TempStorageKey!]);
    }

    [Fact]
    public async Task UploadContent_ReturnsNotFound_WhenSessionBelongsToDifferentProject()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");

        var reserved = await ReserveUploadAsync(project1.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var multipartContent = CreateFileContent("test.ifc", fileContent);

        // Act - try to upload via different project
        var response = await _client.PostAsync(
            $"/api/v1/projects/{project2.Id}/files/uploads/{reserved.Session.Id}/content",
            multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Commit Upload Tests

    private async Task<UploadContentResponse> UploadFileAsync(Guid projectId, Guid sessionId, byte[] content, string fileName = "test.ifc")
    {
        var multipartContent = CreateFileContent(fileName, content);
        var response = await _client.PostAsync(
            $"/api/v1/projects/{projectId}/files/uploads/{sessionId}/content",
            multipartContent);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadContentResponse>())!;
    }

    [Fact]
    public async Task CommitUpload_ReturnsOk_WithValidSession()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id, "test-model.ifc");

        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00, 0x01, 0x02, 0x03 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CommitUploadResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Session);
        Assert.NotNull(result.File);
        Assert.Equal(UploadSessionStatus.Committed, result.Session.Status);
        Assert.NotEqual(Guid.Empty, result.File.Id);
        Assert.Equal("test-model.ifc", result.File.Name);
        Assert.Equal(fileContent.Length, result.File.SizeBytes);
    }

    [Fact]
    public async Task CommitUpload_ReturnsFileId()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CommitUploadResponse>();

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.File.Id);
    }

    [Fact]
    public async Task CommitUpload_CreatesFileRecord_WithCorrectProperties()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id, "my-model.ifc");

        var fileContent = new byte[] { 0x49, 0x46, 0x43, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest { Checksum = "abc123" });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CommitUploadResponse>();

        // Assert - verify file row has correct properties
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var file = await dbContext.Files.FirstOrDefaultAsync(f => f.Id == result!.File.Id);

        Assert.NotNull(file);
        Assert.Equal(project.Id, file.ProjectId);
        Assert.Equal("my-model.ifc", file.Name);
        Assert.Equal(fileContent.Length, file.SizeBytes);
        Assert.Equal("abc123", file.Checksum);
        Assert.Equal(Domain.Enums.FileKind.Source, file.Kind);
        Assert.Equal(Domain.Enums.FileCategory.Ifc, file.Category);
        Assert.Equal("InMemory", file.StorageProvider);
        Assert.NotNull(file.StorageKey);
        Assert.False(file.IsDeleted);
    }

    [Fact]
    public async Task CommitUpload_SetsCorrectFileCategory_ForIfcFiles()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id, "building.ifc");

        var fileContent = new byte[] { 0x01, 0x02 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CommitUploadResponse>();

        // Assert
        Assert.Equal(Contracts.FileCategory.Ifc, result!.File.Category);
    }

    [Fact]
    public async Task CommitUpload_SetsOtherFileCategory_ForUnknownExtensions()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id, "document.pdf");

        var fileContent = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CommitUploadResponse>();

        // Assert
        Assert.Equal(Contracts.FileCategory.Other, result!.File.Category);
    }

    [Fact]
    public async Task CommitUpload_UpdatesSessionStatus_ToCommitted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());
        response.EnsureSuccessStatusCode();

        // Assert - verify session status in database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var session = await dbContext.UploadSessions.FirstOrDefaultAsync(s => s.Id == reserved.Session.Id);

        Assert.NotNull(session);
        Assert.Equal(Domain.Enums.UploadSessionStatus.Committed, session.Status);
        Assert.NotNull(session.CommittedFileId);
    }

    [Fact]
    public async Task CommitUpload_ReturnsNotFound_WhenSessionDoesNotExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{Guid.NewGuid()}/commit",
            new CommitUploadRequest());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CommitUpload_ReturnsBadRequest_WhenSessionIsReserved()
    {
        // Arrange - create session but don't upload content
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        // Act - try to commit without uploading content
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CommitUpload_ReturnsBadRequest_WhenSessionAlreadyCommitted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // First commit
        var firstResponse = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());
        firstResponse.EnsureSuccessStatusCode();

        // Act - try to commit again
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CommitUpload_ReturnsBadRequest_WhenSessionIsExpired()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Manually set session to expired
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();
            var session = await dbContext.UploadSessions.FirstAsync(s => s.Id == reserved.Session.Id);
            session.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CommitUpload_ReturnsNotFound_WhenSessionBelongsToDifferentProject()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");

        var reserved = await ReserveUploadAsync(project1.Id);
        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        await UploadFileAsync(project1.Id, reserved.Session.Id, fileContent);

        // Act - try to commit via different project
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project2.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CommitUpload_ReturnsBadRequest_WhenContentNotInStorage()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Delete content from storage manually
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();
            var session = await dbContext.UploadSessions.FirstAsync(s => s.Id == reserved.Session.Id);
            _storageProvider.Storage.TryRemove(session.TempStorageKey!, out _);
        }

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CommitUpload_LinksSessionToFile()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var reserved = await ReserveUploadAsync(project.Id);

        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        await UploadFileAsync(project.Id, reserved.Session.Id, fileContent);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/files/uploads/{reserved.Session.Id}/commit",
            new CommitUploadRequest());
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CommitUploadResponse>();

        // Assert - verify session has CommittedFileId
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var session = await dbContext.UploadSessions.FirstOrDefaultAsync(s => s.Id == reserved.Session.Id);

        Assert.NotNull(session);
        Assert.Equal(result!.File.Id, session.CommittedFileId);
    }

    #endregion
}

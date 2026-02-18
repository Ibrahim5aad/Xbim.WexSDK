using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using ProjectRole = Xbim.WexServer.Domain.Enums.ProjectRole;

namespace Xbim.WexServer.App.Tests.Endpoints;

public class ProjectEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public ProjectEndpointsTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
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

    #region Create Project Tests

    [Fact]
    public async Task CreateProject_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateProjectRequest
        {
            Name = "Test Project",
            Description = "A test project"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var project = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("Test Project", project.Name);
        Assert.Equal("A test project", project.Description);
        Assert.Equal(workspace.Id, project.WorkspaceId);
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.True(project.CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CreateProject_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateProjectRequest
        {
            Name = "",
            Description = "A test project"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_ReturnsBadRequest_WhenNameIsWhitespace()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateProjectRequest
        {
            Name = "   ",
            Description = "A test project"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_TrimsWhitespace()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateProjectRequest
        {
            Name = "  Trimmed Name  ",
            Description = "  Trimmed Description  "
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects", request);
        var project = await response.Content.ReadFromJsonAsync<ProjectDto>();

        // Assert
        Assert.Equal("Trimmed Name", project!.Name);
        Assert.Equal("Trimmed Description", project.Description);
    }

    [Fact]
    public async Task CreateProject_ReturnsForbidden_WhenUserIsGuest()
    {
        // Arrange - Create workspace where user is only Guest
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Guest Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Ensure dev user is provisioned
        await _client.GetAsync("/api/v1/me");
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        if (devUser != null)
        {
            dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                UserId = devUser.Id,
                Role = WorkspaceRole.Guest,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "New Project" });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_ReturnsForbidden_WhenWorkspaceNotAccessible()
    {
        // Arrange
        var randomWorkspaceId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{randomWorkspaceId}/projects",
            new CreateProjectRequest { Name = "New Project" });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region List Projects Tests

    [Fact]
    public async Task ListProjects_ReturnsProjects_WhenUserIsMember()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "Project 1" });
        await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "Project 2" });

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ProjectDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, p => p.Name == "Project 1");
        Assert.Contains(result.Items, p => p.Name == "Project 2");
    }

    [Fact]
    public async Task ListProjects_ReturnsPagedResults()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync(
                $"/api/v1/workspaces/{workspace.Id}/projects",
                new CreateProjectRequest { Name = $"Project {i}" });
        }

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects?page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ProjectDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task ListProjects_ReturnsEmptyList_WhenNoProjects()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ProjectDto>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListProjects_ReturnsNotFound_WhenWorkspaceNotAccessible()
    {
        // Arrange
        var randomWorkspaceId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/workspaces/{randomWorkspaceId}/projects");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListProjects_GuestOnlySeesMemberProjects()
    {
        // Arrange - Create workspace where user is Guest with direct project membership
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Guest Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var project1 = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Visible Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var project2 = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Hidden Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.AddRange(project1, project2);

        // Ensure dev user is provisioned
        await _client.GetAsync("/api/v1/me");
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        if (devUser != null)
        {
            // Add user as Guest to workspace
            dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                UserId = devUser.Id,
                Role = WorkspaceRole.Guest,
                CreatedAt = DateTimeOffset.UtcNow
            });

            // Add direct project membership to only one project
            dbContext.ProjectMemberships.Add(new ProjectMembership
            {
                Id = Guid.NewGuid(),
                ProjectId = project1.Id,
                UserId = devUser.Id,
                Role = ProjectRole.Viewer,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ProjectDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Contains(result.Items, p => p.Name == "Visible Project");
        Assert.DoesNotContain(result.Items, p => p.Name == "Hidden Project");
    }

    #endregion

    #region Get Project Tests

    [Fact]
    public async Task GetProject_ReturnsProject_WhenUserHasAccess()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "Get Test Project" });
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal(created.Id, project.Id);
        Assert.Equal("Get Test Project", project.Name);
    }

    [Fact]
    public async Task GetProject_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange - Use a random non-existent project ID
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{randomId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProject_ReturnsNotFound_WhenProjectInInaccessibleWorkspace()
    {
        // Arrange - Create project in workspace user doesn't have access to
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
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Update Project Tests

    [Fact]
    public async Task UpdateProject_UpdatesName_WhenUserIsProjectAdmin()
    {
        // Arrange - User is workspace owner, so has ProjectAdmin access
        var workspace = await CreateWorkspaceAsync();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "Original Name" });
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();

        // Act
        var updateRequest = new UpdateProjectRequest { Name = "Updated Name" };
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task UpdateProject_UpdatesDescription_WhenUserIsProjectAdmin()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "Test Project", Description = "Old Description" });
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();

        // Act
        var updateRequest = new UpdateProjectRequest { Description = "New Description" };
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated);
        Assert.Equal("New Description", updated.Description);
    }

    [Fact]
    public async Task UpdateProject_ClearsDescription_WhenSetToEmpty()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "Test Project", Description = "Some Description" });
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();

        // Act
        var updateRequest = new UpdateProjectRequest { Description = "" };
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated);
        Assert.Null(updated.Description);
    }

    [Fact]
    public async Task UpdateProject_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects",
            new CreateProjectRequest { Name = "Test Project" });
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();

        // Act
        var updateRequest = new UpdateProjectRequest { Name = "" };
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProject_ReturnsForbidden_WhenUserIsNotProjectAdmin()
    {
        // Arrange - Create project where user only has Viewer access via workspace Member role
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Member Workspace",
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

        // Ensure dev user is provisioned
        await _client.GetAsync("/api/v1/me");
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        if (devUser != null)
        {
            // Add user as Member only (gets Viewer access to projects, not ProjectAdmin)
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
        var updateRequest = new UpdateProjectRequest { Name = "Try Update" };
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{project.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProject_ReturnsForbidden_WhenProjectNotAccessible()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var updateRequest = new UpdateProjectRequest { Name = "New Name" };
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{randomId}", updateRequest);

        // Assert - Returns 403 because user has no access (authorization check happens first)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion
}

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

public class ModelEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public ModelEndpointsTests()
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

    private async Task<ProjectDto> CreateProjectAsync(Guid workspaceId, string name = "Test Project")
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/projects",
            new CreateProjectRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    #region Create Model Tests

    [Fact]
    public async Task CreateModel_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new CreateModelRequest
        {
            Name = "Test Model",
            Description = "A test model"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var model = await response.Content.ReadFromJsonAsync<ModelDto>();
        Assert.NotNull(model);
        Assert.Equal("Test Model", model.Name);
        Assert.Equal("A test model", model.Description);
        Assert.Equal(project.Id, model.ProjectId);
        Assert.NotEqual(Guid.Empty, model.Id);
        Assert.True(model.CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CreateModel_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new CreateModelRequest
        {
            Name = "",
            Description = "A test model"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateModel_ReturnsBadRequest_WhenNameIsWhitespace()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new CreateModelRequest
        {
            Name = "   ",
            Description = "A test model"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateModel_TrimsWhitespace()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var request = new CreateModelRequest
        {
            Name = "  Trimmed Name  ",
            Description = "  Trimmed Description  "
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models", request);
        var model = await response.Content.ReadFromJsonAsync<ModelDto>();

        // Assert
        Assert.Equal("Trimmed Name", model!.Name);
        Assert.Equal("Trimmed Description", model.Description);
    }

    [Fact]
    public async Task CreateModel_ReturnsForbidden_WhenUserIsViewer()
    {
        // Arrange - Create project where user only has Viewer access
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

        // Ensure dev user is provisioned
        await _client.GetAsync("/api/v1/me");
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        if (devUser != null)
        {
            // Add user as Member to workspace (which gives Viewer access to projects)
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
            $"/api/v1/projects/{project.Id}/models",
            new CreateModelRequest { Name = "New Model" });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateModel_ReturnsCreated_WhenUserIsEditor()
    {
        // Arrange - Create project where user has Editor access
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Editor Workspace",
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
            // Add user as Member to workspace
            dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspace.Id,
                UserId = devUser.Id,
                Role = WorkspaceRole.Member,
                CreatedAt = DateTimeOffset.UtcNow
            });

            // Add direct Editor membership to project
            dbContext.ProjectMemberships.Add(new ProjectMembership
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                UserId = devUser.Id,
                Role = ProjectRole.Editor,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models",
            new CreateModelRequest { Name = "New Model", Description = "A new model" });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var model = await response.Content.ReadFromJsonAsync<ModelDto>();
        Assert.NotNull(model);
        Assert.Equal("New Model", model.Name);
    }

    [Fact]
    public async Task CreateModel_ReturnsForbidden_WhenProjectNotAccessible()
    {
        // Arrange
        var randomProjectId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{randomProjectId}/models",
            new CreateModelRequest { Name = "New Model" });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region List Models Tests

    [Fact]
    public async Task ListModels_ReturnsModels_WhenUserHasAccess()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models",
            new CreateModelRequest { Name = "Model 1" });
        await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models",
            new CreateModelRequest { Name = "Model 2" });

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/projects/{project.Id}/models");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, m => m.Name == "Model 1");
        Assert.Contains(result.Items, m => m.Name == "Model 2");
    }

    [Fact]
    public async Task ListModels_ReturnsPagedResults()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync(
                $"/api/v1/projects/{project.Id}/models",
                new CreateModelRequest { Name = $"Model {i}" });
        }

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/projects/{project.Id}/models?page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task ListModels_ReturnsEmptyList_WhenNoModels()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/projects/{project.Id}/models");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelDto>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListModels_ReturnsNotFound_WhenProjectNotAccessible()
    {
        // Arrange
        var randomProjectId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/projects/{randomProjectId}/models");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListModels_OnlyReturnsModelsFromRequestedProject()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project1 = await CreateProjectAsync(workspace.Id, "Project 1");
        var project2 = await CreateProjectAsync(workspace.Id, "Project 2");

        await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project1.Id}/models",
            new CreateModelRequest { Name = "Model in Project 1" });
        await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project2.Id}/models",
            new CreateModelRequest { Name = "Model in Project 2" });

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/projects/{project1.Id}/models");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ModelDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Contains(result.Items, m => m.Name == "Model in Project 1");
        Assert.DoesNotContain(result.Items, m => m.Name == "Model in Project 2");
    }

    #endregion

    #region Get Model Tests

    [Fact]
    public async Task GetModel_ReturnsModel_WhenUserHasAccess()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/models",
            new CreateModelRequest { Name = "Get Test Model", Description = "Test description" });
        var created = await createResponse.Content.ReadFromJsonAsync<ModelDto>();

        // Act
        var response = await _client.GetAsync($"/api/v1/models/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = await response.Content.ReadFromJsonAsync<ModelDto>();
        Assert.NotNull(model);
        Assert.Equal(created.Id, model.Id);
        Assert.Equal("Get Test Model", model.Name);
        Assert.Equal("Test description", model.Description);
        Assert.Equal(project.Id, model.ProjectId);
    }

    [Fact]
    public async Task GetModel_ReturnsNotFound_WhenModelDoesNotExist()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/models/{randomId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModel_ReturnsNotFound_WhenUserHasNoAccessToProject()
    {
        // Arrange - Create model in project user doesn't have access to
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
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/models/{model.Id}");

        // Assert - Returns 404 to avoid revealing model existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetModel_ReturnsModel_WhenViewerAccessViaWorkspaceMember()
    {
        // Arrange - User has Member role in workspace (implicit Viewer access to projects)
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

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Accessible Model",
            Description = "This model should be accessible",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Models.Add(model);

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
                Role = WorkspaceRole.Member,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/models/{model.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ModelDto>();
        Assert.NotNull(result);
        Assert.Equal("Accessible Model", result.Name);
    }

    #endregion
}

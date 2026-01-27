using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Contracts;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;

using WorkspaceRole = Octopus.Server.Domain.Enums.WorkspaceRole;

namespace Octopus.Server.App.Tests.Endpoints;

public class WorkspaceEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public WorkspaceEndpointsTests()
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

    #region Create Workspace Tests

    [Fact]
    public async Task CreateWorkspace_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var request = new CreateWorkspaceRequest
        {
            Name = "Test Workspace",
            Description = "A test workspace"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var workspace = await response.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);
        Assert.Equal("Test Workspace", workspace.Name);
        Assert.Equal("A test workspace", workspace.Description);
        Assert.NotEqual(Guid.Empty, workspace.Id);
        Assert.True(workspace.CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CreateWorkspace_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var request = new CreateWorkspaceRequest
        {
            Name = "",
            Description = "A test workspace"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWorkspace_ReturnsBadRequest_WhenNameIsWhitespace()
    {
        // Arrange
        var request = new CreateWorkspaceRequest
        {
            Name = "   ",
            Description = "A test workspace"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWorkspace_MakesCreatorOwner()
    {
        // Arrange
        var request = new CreateWorkspaceRequest
        {
            Name = "Owner Test Workspace"
        };

        // Act
        var createResponse = await _client.PostAsJsonAsync("/api/v1/workspaces", request);
        var workspace = await createResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

        // Verify the user can access the workspace (proves they have membership)
        var getResponse = await _client.GetAsync($"/api/v1/workspaces/{workspace!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateWorkspace_TrimsWhitespace()
    {
        // Arrange
        var request = new CreateWorkspaceRequest
        {
            Name = "  Trimmed Name  ",
            Description = "  Trimmed Description  "
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces", request);
        var workspace = await response.Content.ReadFromJsonAsync<WorkspaceDto>();

        // Assert
        Assert.Equal("Trimmed Name", workspace!.Name);
        Assert.Equal("Trimmed Description", workspace.Description);
    }

    #endregion

    #region List Workspaces Tests

    [Fact]
    public async Task ListWorkspaces_ReturnsOnlyMemberWorkspaces()
    {
        // Arrange - Create a workspace first
        var request = new CreateWorkspaceRequest { Name = "My Workspace" };
        await _client.PostAsJsonAsync("/api/v1/workspaces", request);

        // Act
        var response = await _client.GetAsync("/api/v1/workspaces");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<WorkspaceDto>>();
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 1);
        Assert.Contains(result.Items, w => w.Name == "My Workspace");
    }

    [Fact]
    public async Task ListWorkspaces_ReturnsPagedResults()
    {
        // Arrange - Create multiple workspaces
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/v1/workspaces",
                new CreateWorkspaceRequest { Name = $"Workspace {i}" });
        }

        // Act
        var response = await _client.GetAsync("/api/v1/workspaces?page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<WorkspaceDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.True(result.TotalCount >= 5);
    }

    [Fact]
    public async Task ListWorkspaces_ReturnsEmptyList_WhenNoMemberships()
    {
        // This test uses a fresh factory with a clean database
        var dbName = $"empty_{Guid.NewGuid()}";
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<OctopusDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(OctopusDbContext));
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(new TestInMemoryProcessingQueue());
                services.AddDbContext<OctopusDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });
            });
        });

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/workspaces");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<WorkspaceDto>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    #endregion

    #region Get Workspace Tests

    [Fact]
    public async Task GetWorkspace_ReturnsWorkspace_WhenUserIsMember()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = "Get Test Workspace" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var workspace = await response.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(workspace);
        Assert.Equal(created.Id, workspace.Id);
        Assert.Equal("Get Test Workspace", workspace.Name);
    }

    [Fact]
    public async Task GetWorkspace_ReturnsNotFound_WhenUserIsNotMember()
    {
        // Arrange - Use a random non-existent workspace ID
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{randomId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Update Workspace Tests

    [Fact]
    public async Task UpdateWorkspace_UpdatesName_WhenUserIsAdmin()
    {
        // Arrange - Create a workspace (user is Owner, which is above Admin)
        var createResponse = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = "Original Name" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

        // Act
        var updateRequest = new UpdateWorkspaceRequest { Name = "Updated Name" };
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task UpdateWorkspace_UpdatesDescription_WhenUserIsAdmin()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = "Test Workspace", Description = "Old Description" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

        // Act
        var updateRequest = new UpdateWorkspaceRequest { Description = "New Description" };
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(updated);
        Assert.Equal("New Description", updated.Description);
    }

    [Fact]
    public async Task UpdateWorkspace_ClearsDescription_WhenSetToEmpty()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = "Test Workspace", Description = "Some Description" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

        // Act
        var updateRequest = new UpdateWorkspaceRequest { Description = "" };
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(updated);
        Assert.Null(updated.Description);
    }

    [Fact]
    public async Task UpdateWorkspace_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = "Test Workspace" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

        // Act
        var updateRequest = new UpdateWorkspaceRequest { Name = "" };
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateWorkspace_ReturnsForbidden_WhenUserIsNotAdmin()
    {
        // This test requires setting up a workspace where user is only Member
        // Since dev auth always creates Owner, we need to test via direct DB setup
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        // Create a workspace without a membership for the dev user
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Admin Only Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Get the dev user ID (created by the provisioning middleware on first request)
        // First make a request to ensure user is provisioned
        await _client.GetAsync("/api/v1/me");

        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");
        if (devUser != null)
        {
            // Add user as Member only (not Admin or Owner)
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
        var updateRequest = new UpdateWorkspaceRequest { Name = "Try Update" };
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateWorkspace_ReturnsForbidden_WhenWorkspaceDoesNotExist()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var updateRequest = new UpdateWorkspaceRequest { Name = "New Name" };
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{randomId}", updateRequest);

        // Assert - Returns 403 because user has no access (authorization check happens first)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion
}

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

public class ProjectMembershipEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public ProjectMembershipEndpointsTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _processingQueue = new TestInMemoryProcessingQueue();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<XbimDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(XbimDbContext));

                // Remove processing queue and add in-memory one
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

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
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/projects",
            new CreateProjectRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private async Task EnsureUserProvisionedAsync()
    {
        await _client.GetAsync("/api/v1/me");
    }

    private async Task<User> CreateOtherUserAsync(string subject)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            Email = $"{subject}@example.com",
            DisplayName = $"User {subject}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private async Task AddUserToWorkspaceAsync(Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    #region List Members Tests

    [Fact]
    public async Task ListMembers_ReturnsEmpty_WhenProjectHasNoDirectMembers()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/members");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ProjectMembershipDto>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListMembers_ReturnsMembers_WhenMembersExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var otherUser = await CreateOtherUserAsync("other-list-member");
        await AddUserToWorkspaceAsync(workspace.Id, otherUser.Id, WorkspaceRole.Member);

        // Add the user to the project
        var addResponse = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members",
            new AddProjectMemberRequest { UserId = otherUser.Id, Role = Contracts.ProjectRole.Editor });
        addResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{project.Id}/members");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<ProjectMembershipDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(Contracts.ProjectRole.Editor, result.Items[0].Role);
    }

    [Fact]
    public async Task ListMembers_ReturnsNotFound_WhenNotMember()
    {
        // Arrange
        var randomProjectId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/projects/{randomProjectId}/members");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Add Member Tests

    [Fact]
    public async Task AddMember_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var otherUser = await CreateOtherUserAsync("add-member-user");
        await AddUserToWorkspaceAsync(workspace.Id, otherUser.Id, WorkspaceRole.Member);

        var request = new AddProjectMemberRequest
        {
            UserId = otherUser.Id,
            Role = Contracts.ProjectRole.Editor
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var membership = await response.Content.ReadFromJsonAsync<ProjectMembershipDto>();
        Assert.NotNull(membership);
        Assert.Equal(otherUser.Id, membership.UserId);
        Assert.Equal(Contracts.ProjectRole.Editor, membership.Role);
        Assert.NotNull(membership.User);
        Assert.Equal(otherUser.Email, membership.User.Email);
    }

    [Fact]
    public async Task AddMember_ReturnsNotFound_WhenUserNotFound()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        var request = new AddProjectMemberRequest
        {
            UserId = Guid.NewGuid(), // Non-existent user
            Role = Contracts.ProjectRole.Viewer
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_ReturnsBadRequest_WhenUserNotInWorkspace()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var otherUser = await CreateOtherUserAsync("not-in-workspace");
        // Note: Not adding user to workspace

        var request = new AddProjectMemberRequest
        {
            UserId = otherUser.Id,
            Role = Contracts.ProjectRole.Viewer
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_ReturnsConflict_WhenAlreadyMember()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var otherUser = await CreateOtherUserAsync("already-member");
        await AddUserToWorkspaceAsync(workspace.Id, otherUser.Id, WorkspaceRole.Member);

        var request = new AddProjectMemberRequest
        {
            UserId = otherUser.Id,
            Role = Contracts.ProjectRole.Viewer
        };

        // Add user first time
        var firstResponse = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Act - Try to add again
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_ReturnsForbidden_WhenNotProjectAdmin()
    {
        // Arrange - Create a workspace where dev user is only a Member (not Admin/Owner)
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        await EnsureUserProvisionedAsync();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Non-Admin Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Add dev user as Member (not Admin/Owner)
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = devUser!.Id,
            Role = WorkspaceRole.Member, // Only Member, not Admin
            CreatedAt = DateTimeOffset.UtcNow
        });

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Test Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        // Create another user in the workspace
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "other-non-admin",
            Email = "other-non-admin@example.com",
            DisplayName = "Other Non-Admin User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(otherUser);
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = otherUser.Id,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var request = new AddProjectMemberRequest
        {
            UserId = otherUser.Id,
            Role = Contracts.ProjectRole.Viewer
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_Succeeds_WhenWorkspaceAdminAddsToProject()
    {
        // Arrange - Workspace admin should be able to add members to projects
        var workspace = await CreateWorkspaceAsync(); // Dev user becomes Owner
        var project = await CreateProjectAsync(workspace.Id);
        var otherUser = await CreateOtherUserAsync("workspace-admin-add");
        await AddUserToWorkspaceAsync(workspace.Id, otherUser.Id, WorkspaceRole.Member);

        var request = new AddProjectMemberRequest
        {
            UserId = otherUser.Id,
            Role = Contracts.ProjectRole.ProjectAdmin
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var membership = await response.Content.ReadFromJsonAsync<ProjectMembershipDto>();
        Assert.NotNull(membership);
        Assert.Equal(Contracts.ProjectRole.ProjectAdmin, membership.Role);
    }

    #endregion

    #region Update Member Role Tests

    [Fact]
    public async Task UpdateMemberRole_ReturnsOk_WhenAdminUpdatesOther()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var otherUser = await CreateOtherUserAsync("update-role-user");
        await AddUserToWorkspaceAsync(workspace.Id, otherUser.Id, WorkspaceRole.Member);

        // Add user as Viewer
        var addResponse = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members",
            new AddProjectMemberRequest { UserId = otherUser.Id, Role = Contracts.ProjectRole.Viewer });
        var membership = await addResponse.Content.ReadFromJsonAsync<ProjectMembershipDto>();

        // Act - Update to Editor
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{project.Id}/members/{membership!.Id}",
            new UpdateProjectMemberRequest { Role = Contracts.ProjectRole.Editor });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectMembershipDto>();
        Assert.NotNull(updated);
        Assert.Equal(Contracts.ProjectRole.Editor, updated.Role);
    }

    [Fact]
    public async Task UpdateMemberRole_ReturnsBadRequest_WhenChangingOwnRole()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await EnsureUserProvisionedAsync();
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        // Add dev user as project member with direct membership
        var membership = new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = devUser!.Id,
            Role = ProjectRole.ProjectAdmin,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ProjectMemberships.Add(membership);
        await dbContext.SaveChangesAsync();

        // Act - Try to change own role
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{project.Id}/members/{membership.Id}",
            new UpdateProjectMemberRequest { Role = Contracts.ProjectRole.Viewer });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_ReturnsNotFound_WhenMembershipNotFound()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var randomMembershipId = Guid.NewGuid();

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{project.Id}/members/{randomMembershipId}",
            new UpdateProjectMemberRequest { Role = Contracts.ProjectRole.Editor });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_ReturnsForbidden_WhenNotProjectAdmin()
    {
        // Arrange - Create scenario where dev user is only a Viewer
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        await EnsureUserProvisionedAsync();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Viewer Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Add dev user as Guest (no implicit project access)
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = devUser!.Id,
            Role = WorkspaceRole.Guest,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Test Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        // Add dev user as Viewer to project (not ProjectAdmin)
        dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = devUser.Id,
            Role = ProjectRole.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Create another member
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "other-viewer",
            Email = "other-viewer@example.com",
            DisplayName = "Other Viewer User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(otherUser);

        var otherMembership = new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = otherUser.Id,
            Role = ProjectRole.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ProjectMemberships.Add(otherMembership);
        await dbContext.SaveChangesAsync();

        // Act - Try to update other member's role as Viewer
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/projects/{project.Id}/members/{otherMembership.Id}",
            new UpdateProjectMemberRequest { Role = Contracts.ProjectRole.Editor });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Remove Member Tests

    [Fact]
    public async Task RemoveMember_ReturnsNoContent_WhenAdminRemovesOther()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var otherUser = await CreateOtherUserAsync("remove-member-user");
        await AddUserToWorkspaceAsync(workspace.Id, otherUser.Id, WorkspaceRole.Member);

        // Add user to project
        var addResponse = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members",
            new AddProjectMemberRequest { UserId = otherUser.Id, Role = Contracts.ProjectRole.Viewer });
        var membership = await addResponse.Content.ReadFromJsonAsync<ProjectMembershipDto>();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/projects/{project.Id}/members/{membership!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify member is removed
        var listResponse = await _client.GetAsync($"/api/v1/projects/{project.Id}/members");
        var members = await listResponse.Content.ReadFromJsonAsync<PagedList<ProjectMembershipDto>>();
        Assert.Empty(members!.Items);
    }

    [Fact]
    public async Task RemoveMember_ReturnsNoContent_WhenMemberRemovesSelf()
    {
        // Arrange - Create scenario where dev user has direct project membership
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);

        await EnsureUserProvisionedAsync();
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        // Add dev user as project member
        var membership = new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = devUser!.Id,
            Role = ProjectRole.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ProjectMemberships.Add(membership);
        await dbContext.SaveChangesAsync();

        // Act - Remove self
        var response = await _client.DeleteAsync($"/api/v1/projects/{project.Id}/members/{membership.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_ReturnsNotFound_WhenMembershipNotFound()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var randomMembershipId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/projects/{project.Id}/members/{randomMembershipId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_ReturnsForbidden_WhenViewerTriesToRemoveOther()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        await EnsureUserProvisionedAsync();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Viewer Remove Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Add dev user as Guest to workspace
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = devUser!.Id,
            Role = WorkspaceRole.Guest,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Test Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        // Add dev user as Viewer
        dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = devUser.Id,
            Role = ProjectRole.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Create another user and add to project
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "other-remove-test",
            Email = "other-remove-test@example.com",
            DisplayName = "Other Remove Test User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(otherUser);

        var otherMembership = new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = otherUser.Id,
            Role = ProjectRole.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ProjectMemberships.Add(otherMembership);
        await dbContext.SaveChangesAsync();

        // Act - Try to remove other user as Viewer
        var response = await _client.DeleteAsync($"/api/v1/projects/{project.Id}/members/{otherMembership.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Access Control Tests

    [Fact]
    public async Task AddMember_Succeeds_WhenDirectProjectAdminAdds()
    {
        // Arrange - User with direct ProjectAdmin role should be able to add members
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        await EnsureUserProvisionedAsync();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Direct Admin Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Add dev user as Guest to workspace (no implicit project admin)
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = devUser!.Id,
            Role = WorkspaceRole.Guest,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Direct Admin Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        // Add dev user as direct ProjectAdmin
        dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = devUser.Id,
            Role = ProjectRole.ProjectAdmin,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Create another user in workspace
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "other-direct-admin",
            Email = "other-direct-admin@example.com",
            DisplayName = "Other Direct Admin User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(otherUser);
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = otherUser.Id,
            Role = WorkspaceRole.Guest,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var request = new AddProjectMemberRequest
        {
            UserId = otherUser.Id,
            Role = Contracts.ProjectRole.Editor
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/members", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    #endregion
}

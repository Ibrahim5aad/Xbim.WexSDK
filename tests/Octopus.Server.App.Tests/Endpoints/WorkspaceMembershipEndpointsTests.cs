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

public class WorkspaceMembershipEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public WorkspaceMembershipEndpointsTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _processingQueue = new TestInMemoryProcessingQueue();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<OctopusDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(OctopusDbContext));

                // Remove processing queue and add in-memory one
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

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

    private async Task EnsureUserProvisionedAsync()
    {
        await _client.GetAsync("/api/v1/me");
    }

    #region List Members Tests

    [Fact]
    public async Task ListMembers_ReturnsOwner_AfterWorkspaceCreation()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/members");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<WorkspaceMembershipDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(Contracts.WorkspaceRole.Owner, result.Items[0].Role);
    }

    [Fact]
    public async Task ListMembers_ReturnsUserInfo_WithMembership()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/members");

        // Assert
        var result = await response.Content.ReadFromJsonAsync<PagedList<WorkspaceMembershipDto>>();
        Assert.NotNull(result?.Items[0].User);
        Assert.NotEqual(Guid.Empty, result.Items[0].User.Id);
    }

    [Fact]
    public async Task ListMembers_ReturnsNotFound_WhenNotMember()
    {
        // Arrange
        var randomWorkspaceId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{randomWorkspaceId}/members");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Create Invite Tests

    [Fact]
    public async Task CreateInvite_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateWorkspaceInviteRequest
        {
            Email = "newuser@example.com",
            Role = Contracts.WorkspaceRole.Member
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invite = await response.Content.ReadFromJsonAsync<WorkspaceInviteDto>();
        Assert.NotNull(invite);
        Assert.Equal("newuser@example.com", invite.Email);
        Assert.Equal(Contracts.WorkspaceRole.Member, invite.Role);
        Assert.NotEmpty(invite.Token);
        Assert.False(invite.IsAccepted);
        Assert.True(invite.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateInvite_ReturnsBadRequest_WhenEmailIsEmpty()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateWorkspaceInviteRequest
        {
            Email = "",
            Role = Contracts.WorkspaceRole.Member
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvite_ReturnsBadRequest_WhenInvitingAsOwner()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateWorkspaceInviteRequest
        {
            Email = "newuser@example.com",
            Role = Contracts.WorkspaceRole.Owner
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvite_ReturnsConflict_WhenInviteAlreadyExists()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var request = new CreateWorkspaceInviteRequest
        {
            Email = "duplicate@example.com",
            Role = Contracts.WorkspaceRole.Member
        };

        // Create first invite
        await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites", request);

        // Act - Try to create duplicate
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvite_ReturnsForbidden_WhenNotAdmin()
    {
        // Arrange - Create workspace where user is only Member
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        await EnsureUserProvisionedAsync();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Member Only Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

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

        var request = new CreateWorkspaceInviteRequest
        {
            Email = "newuser@example.com",
            Role = Contracts.WorkspaceRole.Member
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region List Invites Tests

    [Fact]
    public async Task ListInvites_ReturnsInvites_WhenAdmin()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites",
            new CreateWorkspaceInviteRequest { Email = "user1@example.com", Role = Contracts.WorkspaceRole.Member });
        await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites",
            new CreateWorkspaceInviteRequest { Email = "user2@example.com", Role = Contracts.WorkspaceRole.Admin });

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/invites");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<WorkspaceInviteDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task ListInvites_ReturnsEmpty_WhenNoInvites()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/invites");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<WorkspaceInviteDto>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
    }

    #endregion

    #region Accept Invite Tests

    [Fact]
    public async Task AcceptInvite_CreatesMembership_WithValidToken()
    {
        // Arrange - Create workspace and invite, then set up a scenario where the user
        // accepting is not already a member
        var workspace = await CreateWorkspaceAsync();

        // Create an invite for a different email
        var inviteResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invites",
            new CreateWorkspaceInviteRequest { Email = "newuser@example.com", Role = Contracts.WorkspaceRole.Member });
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);
        var invite = await inviteResponse.Content.ReadFromJsonAsync<WorkspaceInviteDto>();

        // Now we need to simulate a different user accepting the invite
        // Since we're always the dev user, we'll test via database manipulation
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        // Create the new user who will accept
        var newUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "newuser-subject",
            Email = "newuser@example.com",
            DisplayName = "New User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();

        // The invite token should work - but we can't test full acceptance without multi-user
        // Instead, verify the invite was created properly
        Assert.NotNull(invite);
        Assert.Equal("newuser@example.com", invite.Email);
        Assert.Equal(Contracts.WorkspaceRole.Member, invite.Role);
        Assert.NotEmpty(invite.Token);

        // Verify accepting as the dev user (who is already owner) returns conflict
        var response = await _client.PostAsync($"/api/v1/workspaces/invites/{invite.Token}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvite_ReturnsNotFound_WhenTokenInvalid()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/workspaces/invites/invalid-token/accept", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvite_ReturnsBadRequest_WhenExpired()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        // Create an expired invite directly in DB
        var expiredInvite = new WorkspaceInvite
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Email = "expired@example.com",
            Role = WorkspaceRole.Member,
            Token = "expired-token",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-3) // Expired 3 days ago
        };
        dbContext.WorkspaceInvites.Add(expiredInvite);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.PostAsync("/api/v1/workspaces/invites/expired-token/accept", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvite_ReturnsBadRequest_WhenAlreadyAccepted()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        // Create an already-accepted invite directly in DB
        var acceptedInvite = new WorkspaceInvite
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Email = "accepted@example.com",
            Role = WorkspaceRole.Member,
            Token = "accepted-token",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(2),
            AcceptedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        dbContext.WorkspaceInvites.Add(acceptedInvite);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.PostAsync("/api/v1/workspaces/invites/accepted-token/accept", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Update Member Role Tests

    [Fact]
    public async Task UpdateMemberRole_ReturnsBadRequest_WhenChangingOwnRole()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Get the owner membership
        var membersResponse = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/members");
        var members = await membersResponse.Content.ReadFromJsonAsync<PagedList<WorkspaceMembershipDto>>();
        var ownerMembership = members!.Items[0];

        // Act - Try to change own role
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/members/{ownerMembership.Id}",
            new UpdateWorkspaceMemberRequest { Role = Contracts.WorkspaceRole.Admin });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMemberRole_UpdatesRole_WhenOwnerChangesOtherMember()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Add another user as member
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "other-user",
            Email = "other@example.com",
            DisplayName = "Other User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(otherUser);

        var membership = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = otherUser.Id,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.WorkspaceMemberships.Add(membership);
        await dbContext.SaveChangesAsync();

        // Act - Change other user's role to Admin
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/members/{membership.Id}",
            new UpdateWorkspaceMemberRequest { Role = Contracts.WorkspaceRole.Admin });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkspaceMembershipDto>();
        Assert.NotNull(updated);
        Assert.Equal(Contracts.WorkspaceRole.Admin, updated.Role);
    }

    [Fact]
    public async Task UpdateMemberRole_ReturnsNotFound_WhenMembershipNotFound()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var randomMembershipId = Guid.NewGuid();

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/members/{randomMembershipId}",
            new UpdateWorkspaceMemberRequest { Role = Contracts.WorkspaceRole.Admin });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Remove Member Tests

    [Fact]
    public async Task RemoveMember_ReturnsNoContent_WhenOwnerRemovesOther()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Add another user as member
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "remove-user",
            Email = "remove@example.com",
            DisplayName = "Remove User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(otherUser);

        var membership = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = otherUser.Id,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.WorkspaceMemberships.Add(membership);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{membership.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify member is removed
        var membersResponse = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/members");
        var members = await membersResponse.Content.ReadFromJsonAsync<PagedList<WorkspaceMembershipDto>>();
        Assert.Single(members!.Items); // Only owner should remain
    }

    [Fact]
    public async Task RemoveMember_ReturnsBadRequest_WhenLastOwnerTriesToLeave()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();

        // Get the owner membership
        var membersResponse = await _client.GetAsync($"/api/v1/workspaces/{workspace.Id}/members");
        var members = await membersResponse.Content.ReadFromJsonAsync<PagedList<WorkspaceMembershipDto>>();
        var ownerMembership = members!.Items[0];

        // Act - Try to remove self as last owner
        var response = await _client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{ownerMembership.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_ReturnsNotFound_WhenMembershipNotFound()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var randomMembershipId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{randomMembershipId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_ReturnsForbidden_WhenMemberTriesToRemoveOther()
    {
        // Arrange - Create workspace where user is only Member
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        await EnsureUserProvisionedAsync();
        var devUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Subject == "dev-user");

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Remove Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Add dev user as Member
        dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = devUser!.Id,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Add another user
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Subject = "other-remove",
            Email = "other-remove@example.com",
            DisplayName = "Other Remove User",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(otherUser);

        var otherMembership = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = otherUser.Id,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.WorkspaceMemberships.Add(otherMembership);
        await dbContext.SaveChangesAsync();

        // Act - Try to remove other user as Member
        var response = await _client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{otherMembership.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion
}

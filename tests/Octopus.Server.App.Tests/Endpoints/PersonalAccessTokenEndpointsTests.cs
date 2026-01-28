using System.Net;
using System.Net.Http.Headers;
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

public class PersonalAccessTokenEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public PersonalAccessTokenEndpointsTests()
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

    private async Task<(Guid WorkspaceId, Guid UserId)> CreateTestWorkspaceWithMemberUser()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        // The dev user is auto-provisioned with a specific ID
        var devUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == devUserId);
        if (user == null)
        {
            user = new User
            {
                Id = devUserId,
                Subject = "dev-user",
                Email = "dev@example.com",
                DisplayName = "Dev User",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Users.Add(user);
        }

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var membership = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = devUserId,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.WorkspaceMemberships.Add(membership);

        await dbContext.SaveChangesAsync();

        return (workspace.Id, devUserId);
    }

    private async Task<(Guid WorkspaceId, Guid UserId)> CreateTestWorkspaceWithAdminUser()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();

        var devUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == devUserId);
        if (user == null)
        {
            user = new User
            {
                Id = devUserId,
                Subject = "dev-user",
                Email = "dev@example.com",
                DisplayName = "Dev User",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Users.Add(user);
        }

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var membership = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = devUserId,
            Role = WorkspaceRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.WorkspaceMemberships.Add(membership);

        await dbContext.SaveChangesAsync();

        return (workspace.Id, devUserId);
    }

    #region Create PAT Tests

    [Fact]
    public async Task CreatePat_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var request = new CreatePersonalAccessTokenRequest
        {
            Name = "Test PAT",
            Description = "A test PAT for CI/CD",
            Scopes = new[] { "read", "write" },
            ExpiresInDays = 30
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var pat = await response.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();
        Assert.NotNull(pat);
        Assert.Equal("Test PAT", pat.Name);
        Assert.StartsWith("ocpat_", pat.Token);
        Assert.StartsWith("ocpat_", pat.TokenPrefix);
        Assert.Equal(workspaceId, pat.WorkspaceId);
        Assert.Contains("read", pat.Scopes);
        Assert.Contains("write", pat.Scopes);
    }

    [Fact]
    public async Task CreatePat_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var request = new CreatePersonalAccessTokenRequest
        {
            Name = "",
            Scopes = new[] { "read" }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePat_ReturnsBadRequest_WhenScopesAreEmpty()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var request = new CreatePersonalAccessTokenRequest
        {
            Name = "Test PAT",
            Scopes = Array.Empty<string>()
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePat_ReturnsBadRequest_WhenExpirationExceedsMax()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var request = new CreatePersonalAccessTokenRequest
        {
            Name = "Test PAT",
            Scopes = new[] { "read" },
            ExpiresInDays = 500 // Exceeds max of 365
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePat_CreatesAuditLog()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var request = new CreatePersonalAccessTokenRequest
        {
            Name = "Audit Test PAT",
            Scopes = new[] { "read" },
            ExpiresInDays = 30
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var pat = await response.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();
        Assert.NotNull(pat);

        // Get audit logs
        var auditResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats/{pat.Id}/audit-logs");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<PagedList<PersonalAccessTokenAuditLogDto>>();
        Assert.NotNull(auditLogs);
        Assert.Single(auditLogs.Items);
        Assert.Equal(PersonalAccessTokenAuditEventType.Created, auditLogs.Items[0].EventType);
    }

    #endregion

    #region List PATs Tests

    [Fact]
    public async Task ListPats_ReturnsPagedList()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Create multiple PATs
        for (int i = 0; i < 3; i++)
        {
            var request = new CreatePersonalAccessTokenRequest
            {
                Name = $"PAT {i}",
                Scopes = new[] { "read" }
            };
            await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats", request);
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pats = await response.Content.ReadFromJsonAsync<PagedList<PersonalAccessTokenDto>>();
        Assert.NotNull(pats);
        Assert.Equal(3, pats.TotalCount);
        Assert.Equal(3, pats.Items.Count);
    }

    [Fact]
    public async Task ListPats_ExcludesRevokedByDefault()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Create a PAT
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "To Revoke",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Revoke it
        await _client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/pats/{created!.Id}");

        // Create another active PAT
        await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "Active",
                Scopes = new[] { "read" }
            });

        // Act - list without includeRevoked
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats");
        var pats = await response.Content.ReadFromJsonAsync<PagedList<PersonalAccessTokenDto>>();

        // Assert
        Assert.Single(pats!.Items);
        Assert.Equal("Active", pats.Items[0].Name);
    }

    #endregion

    #region Get PAT Tests

    [Fact]
    public async Task GetPat_ReturnsPat_WhenExists()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "Get Test PAT",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pat = await response.Content.ReadFromJsonAsync<PersonalAccessTokenDto>();
        Assert.NotNull(pat);
        Assert.Equal("Get Test PAT", pat.Name);
    }

    [Fact]
    public async Task GetPat_ReturnsNotFound_WhenDoesNotExist()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Update PAT Tests

    [Fact]
    public async Task UpdatePat_UpdatesName()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "Original Name",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        var updateRequest = new UpdatePersonalAccessTokenRequest
        {
            Name = "Updated Name"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pat = await response.Content.ReadFromJsonAsync<PersonalAccessTokenDto>();
        Assert.NotNull(pat);
        Assert.Equal("Updated Name", pat.Name);
    }

    [Fact]
    public async Task UpdatePat_ReturnsBadRequest_WhenRevoked()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "To Revoke",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Revoke it
        await _client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/pats/{created!.Id}");

        // Try to update
        var updateRequest = new UpdatePersonalAccessTokenRequest
        {
            Name = "New Name"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats/{created.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Revoke PAT Tests

    [Fact]
    public async Task RevokePat_ReturnsNoContent()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "To Revoke",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/pats/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's revoked
        var getResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats/{created.Id}");
        var pat = await getResponse.Content.ReadFromJsonAsync<PersonalAccessTokenDto>();
        Assert.True(pat!.IsRevoked);
        Assert.False(pat.IsActive);
    }

    [Fact]
    public async Task RevokePat_CreatesAuditLog()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "To Revoke",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Act
        await _client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/pats/{created!.Id}");

        // Assert - check audit log
        var auditResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats/{created.Id}/audit-logs");
        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<PagedList<PersonalAccessTokenAuditLogDto>>();
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Items, l => l.EventType == PersonalAccessTokenAuditEventType.RevokedByUser);
    }

    #endregion

    #region Admin Endpoints Tests

    [Fact]
    public async Task AdminListPats_ReturnsAllWorkspacePats()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithAdminUser();

        // Create a PAT
        await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "Admin Test PAT",
                Scopes = new[] { "read" }
            });

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/admin/pats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pats = await response.Content.ReadFromJsonAsync<PagedList<WorkspacePersonalAccessTokenSummaryDto>>();
        Assert.NotNull(pats);
        Assert.Single(pats.Items);
        Assert.NotNull(pats.Items[0].UserEmail);
    }

    [Fact]
    public async Task AdminListPats_ReturnsForbidden_ForNonAdmin()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/admin/pats");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminRevokePat_RevokesOtherUsersPat()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithAdminUser();

        // Create a PAT (as dev user who is admin)
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "Admin Revoke Test",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Act - admin revoke
        var response = await _client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/admin/pats/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify audit log shows admin revocation
        var auditResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats/{created.Id}/audit-logs");
        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<PagedList<PersonalAccessTokenAuditLogDto>>();
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Items, l => l.EventType == PersonalAccessTokenAuditEventType.RevokedByAdmin);
    }

    #endregion

    #region PAT Authentication Tests

    [Fact]
    public async Task PatAuthentication_Works_WithValidToken()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "Auth Test PAT",
                Scopes = new[] { "read", "write" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Create a new client that uses PAT auth
        var patClient = _factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Token);

        // Act - use the PAT to make a request
        var response = await patClient.GetAsync($"/api/v1/workspaces/{workspaceId}/pats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PatAuthentication_Fails_WithInvalidToken()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Create a client with an invalid PAT
        var patClient = _factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ocpat_invalid_token_here");

        // Act
        var response = await patClient.GetAsync($"/api/v1/workspaces/{workspaceId}/pats");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatAuthentication_Fails_WithRevokedToken()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "Revoke Auth Test",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Revoke the token
        await _client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/pats/{created!.Id}");

        // Create a client with the revoked PAT
        var patClient = _factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);

        // Act
        var response = await patClient.GetAsync($"/api/v1/workspaces/{workspaceId}/pats");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatAuthentication_UpdatesLastUsedAt()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/pats",
            new CreatePersonalAccessTokenRequest
            {
                Name = "LastUsed Test",
                Scopes = new[] { "read" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalAccessTokenCreatedDto>();

        // Create a client with the PAT
        var patClient = _factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Token);

        // Act - use the PAT to make a request
        await patClient.GetAsync($"/api/v1/workspaces/{workspaceId}/pats");

        // Get the PAT details to check LastUsedAt
        var getResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/pats/{created.Id}");
        var pat = await getResponse.Content.ReadFromJsonAsync<PersonalAccessTokenDto>();

        // Assert
        Assert.NotNull(pat!.LastUsedAt);
    }

    #endregion
}

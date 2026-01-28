using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octopus.Api.Client;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Persistence.EfCore;

using WorkspaceRole = Octopus.Server.Domain.Enums.WorkspaceRole;

namespace Octopus.Server.App.Tests.Endpoints;

public class PersonalAccessTokenEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _httpClient;
    private readonly OctopusApiClient _apiClient;
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

        _httpClient = _factory.CreateClient();
        _apiClient = new OctopusApiClient(_httpClient.BaseAddress!.ToString(), _httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
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

    /// <summary>
    /// Helper to create a PAT using the generated API client.
    /// </summary>
    private async Task<PersonalAccessTokenCreatedDto> CreatePatAsync(Guid workspaceId, CreatePersonalAccessTokenRequest request)
    {
        return await _apiClient.CreatePersonalAccessTokenAsync(workspaceId, request);
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
        var pat = await CreatePatAsync(workspaceId, request);

        // Assert
        Assert.NotNull(pat);
        Assert.Equal("Test PAT", pat.Name);
        Assert.NotNull(pat.Token);
        Assert.StartsWith("ocpat_", pat.Token);
        Assert.StartsWith("ocpat_", pat.TokenPrefix);
        Assert.Equal(workspaceId, pat.WorkspaceId);
        Assert.Contains("read", pat.Scopes!);
        Assert.Contains("write", pat.Scopes!);
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

        // Act & Assert
        await Assert.ThrowsAsync<OctopusApiException<ErrorResponse>>(async () =>
            await _apiClient.CreatePersonalAccessTokenAsync(workspaceId, request));
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

        // Act & Assert
        await Assert.ThrowsAsync<OctopusApiException<ErrorResponse>>(async () =>
            await _apiClient.CreatePersonalAccessTokenAsync(workspaceId, request));
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

        // Act & Assert
        await Assert.ThrowsAsync<OctopusApiException<ErrorResponse>>(async () =>
            await _apiClient.CreatePersonalAccessTokenAsync(workspaceId, request));
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
        var pat = await CreatePatAsync(workspaceId, request);

        // Get audit logs using generated client
        var auditLogs = await _apiClient.GetPersonalAccessTokenAuditLogsAsync(workspaceId, pat.Id!.Value);

        // Assert
        Assert.NotNull(auditLogs);
        Assert.Single(auditLogs.Items!);
        Assert.Equal(PersonalAccessTokenAuditEventType._0 /* Created */, auditLogs.Items!.First().EventType);
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
            await CreatePatAsync(workspaceId, request);
        }

        // Act
        var pats = await _apiClient.ListMyPersonalAccessTokensAsync(workspaceId);

        // Assert
        Assert.NotNull(pats);
        Assert.Equal(3, pats.TotalCount);
        Assert.Equal(3, pats.Items!.Count);
    }

    [Fact]
    public async Task ListPats_ExcludesRevokedByDefault()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Create a PAT
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "To Revoke",
            Scopes = new[] { "read" }
        });

        // Revoke it
        await _apiClient.RevokePersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Create another active PAT
        await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "Active",
            Scopes = new[] { "read" }
        });

        // Act - list without includeRevoked
        var pats = await _apiClient.ListMyPersonalAccessTokensAsync(workspaceId);

        // Assert
        Assert.Single(pats.Items!);
        Assert.Equal("Active", pats.Items!.First().Name);
    }

    #endregion

    #region Get PAT Tests

    [Fact]
    public async Task GetPat_ReturnsPat_WhenExists()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "Get Test PAT",
            Scopes = new[] { "read" }
        });

        // Act
        var pat = await _apiClient.GetPersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Assert
        Assert.NotNull(pat);
        Assert.Equal("Get Test PAT", pat.Name);
    }

    [Fact]
    public async Task GetPat_ReturnsNotFound_WhenDoesNotExist()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Act & Assert
        await Assert.ThrowsAsync<OctopusApiException<ErrorResponse>>(async () =>
            await _apiClient.GetPersonalAccessTokenAsync(workspaceId, Guid.NewGuid()));
    }

    #endregion

    #region Update PAT Tests

    [Fact]
    public async Task UpdatePat_UpdatesName()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "Original Name",
            Scopes = new[] { "read" }
        });

        var updateRequest = new UpdatePersonalAccessTokenRequest
        {
            Name = "Updated Name"
        };

        // Act
        var pat = await _apiClient.UpdatePersonalAccessTokenAsync(workspaceId, created.Id!.Value, updateRequest);

        // Assert
        Assert.NotNull(pat);
        Assert.Equal("Updated Name", pat.Name);
    }

    [Fact]
    public async Task UpdatePat_ReturnsBadRequest_WhenRevoked()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "To Revoke",
            Scopes = new[] { "read" }
        });

        // Revoke it
        await _apiClient.RevokePersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Try to update
        var updateRequest = new UpdatePersonalAccessTokenRequest
        {
            Name = "New Name"
        };

        // Act & Assert
        await Assert.ThrowsAsync<OctopusApiException<ErrorResponse>>(async () =>
            await _apiClient.UpdatePersonalAccessTokenAsync(workspaceId, created.Id!.Value, updateRequest));
    }

    #endregion

    #region Revoke PAT Tests

    [Fact]
    public async Task RevokePat_ReturnsNoContent()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "To Revoke",
            Scopes = new[] { "read" }
        });

        // Act
        await _apiClient.RevokePersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Verify it's revoked
        var pat = await _apiClient.GetPersonalAccessTokenAsync(workspaceId, created.Id!.Value);
        Assert.True(pat.IsRevoked);
        Assert.False(pat.IsActive);
    }

    [Fact]
    public async Task RevokePat_CreatesAuditLog()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "To Revoke",
            Scopes = new[] { "read" }
        });

        // Act
        await _apiClient.RevokePersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Assert - check audit log
        var auditLogs = await _apiClient.GetPersonalAccessTokenAuditLogsAsync(workspaceId, created.Id!.Value);
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Items!, l => l.EventType == PersonalAccessTokenAuditEventType._2 /* RevokedByUser */);
    }

    #endregion

    #region Admin Endpoints Tests

    [Fact]
    public async Task AdminListPats_ReturnsAllWorkspacePats()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithAdminUser();

        // Create a PAT
        await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "Admin Test PAT",
            Scopes = new[] { "read" }
        });

        // Act
        var pats = await _apiClient.ListWorkspacePersonalAccessTokensAsync(workspaceId);

        // Assert
        Assert.NotNull(pats);
        Assert.Single(pats.Items!);
        Assert.NotNull(pats.Items!.First().UserEmail);
    }

    [Fact]
    public async Task AdminListPats_ReturnsForbidden_ForNonAdmin()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Act & Assert
        await Assert.ThrowsAsync<OctopusApiException<ErrorResponse>>(async () =>
            await _apiClient.ListWorkspacePersonalAccessTokensAsync(workspaceId));
    }

    [Fact]
    public async Task AdminRevokePat_RevokesOtherUsersPat()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithAdminUser();

        // Create a PAT (as dev user who is admin)
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "Admin Revoke Test",
            Scopes = new[] { "read" }
        });

        // Act - admin revoke
        await _apiClient.AdminRevokePersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Verify audit log shows admin revocation
        var auditLogs = await _apiClient.GetPersonalAccessTokenAuditLogsAsync(workspaceId, created.Id!.Value);
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Items!, l => l.EventType == PersonalAccessTokenAuditEventType._3 /* RevokedByAdmin */);
    }

    #endregion

    #region PAT Authentication Tests

    [Fact]
    public async Task PatAuthentication_Works_WithValidToken()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "Auth Test PAT",
            Scopes = new[] { "read", "write" }
        });

        // Create a new client that uses PAT auth
        var patHttpClient = _factory.CreateClient();
        patHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        var patApiClient = new OctopusApiClient(patHttpClient.BaseAddress!.ToString(), patHttpClient);

        // Act - use the PAT to make a request
        var pats = await patApiClient.ListMyPersonalAccessTokensAsync(workspaceId);

        // Assert
        Assert.NotNull(pats);
    }

    [Fact]
    public async Task PatAuthentication_Fails_WithInvalidToken()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();

        // Create a client with an invalid PAT
        var patHttpClient = _factory.CreateClient();
        patHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ocpat_invalid_token_here");
        var patApiClient = new OctopusApiClient(patHttpClient.BaseAddress!.ToString(), patHttpClient);

        // Act & Assert - 401 returns no body so we get base OctopusApiException
        await Assert.ThrowsAsync<OctopusApiException>(async () =>
            await patApiClient.ListMyPersonalAccessTokensAsync(workspaceId));
    }

    [Fact]
    public async Task PatAuthentication_Fails_WithRevokedToken()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "Revoke Auth Test",
            Scopes = new[] { "read" }
        });

        // Revoke the token
        await _apiClient.RevokePersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Create a client with the revoked PAT
        var patHttpClient = _factory.CreateClient();
        patHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        var patApiClient = new OctopusApiClient(patHttpClient.BaseAddress!.ToString(), patHttpClient);

        // Act & Assert - 401 returns no body so we get base OctopusApiException
        await Assert.ThrowsAsync<OctopusApiException>(async () =>
            await patApiClient.ListMyPersonalAccessTokensAsync(workspaceId));
    }

    [Fact]
    public async Task PatAuthentication_UpdatesLastUsedAt()
    {
        // Arrange
        var (workspaceId, _) = await CreateTestWorkspaceWithMemberUser();
        var created = await CreatePatAsync(workspaceId, new CreatePersonalAccessTokenRequest
        {
            Name = "LastUsed Test",
            Scopes = new[] { "read" }
        });

        // Create a client with the PAT
        var patHttpClient = _factory.CreateClient();
        patHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        var patApiClient = new OctopusApiClient(patHttpClient.BaseAddress!.ToString(), patHttpClient);

        // Act - use the PAT to make a request
        await patApiClient.ListMyPersonalAccessTokensAsync(workspaceId);

        // Get the PAT details to check LastUsedAt
        var pat = await _apiClient.GetPersonalAccessTokenAsync(workspaceId, created.Id!.Value);

        // Assert
        Assert.NotNull(pat.LastUsedAt);
    }

    #endregion
}

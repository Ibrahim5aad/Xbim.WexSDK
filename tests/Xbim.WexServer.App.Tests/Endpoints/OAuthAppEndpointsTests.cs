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
using DomainClientType = Xbim.WexServer.Domain.Enums.OAuthClientType;

namespace Xbim.WexServer.App.Tests.Endpoints;

public class OAuthAppEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public OAuthAppEndpointsTests()
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

    private async Task<Guid> CreateTestWorkspaceWithAdminUser()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

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
            Role = WorkspaceRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.WorkspaceMemberships.Add(membership);

        await dbContext.SaveChangesAsync();

        return workspace.Id;
    }

    #region Create OAuth App Tests

    [Fact]
    public async Task CreateOAuthApp_ReturnsCreated_ForConfidentialClient()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var request = new CreateOAuthAppRequest
        {
            Name = "Test App",
            Description = "A test OAuth app",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = new[] { "https://example.com/callback" },
            AllowedScopes = new[] { "read", "write" }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var app = await response.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();
        Assert.NotNull(app);
        Assert.Equal("Test App", app.Name);
        Assert.Equal(OAuthClientType.Confidential, app.ClientType);
        Assert.NotNull(app.ClientSecret); // Should have secret for confidential client
        Assert.StartsWith("oct_", app.ClientId);
        Assert.True(app.IsEnabled);
    }

    [Fact]
    public async Task CreateOAuthApp_ReturnsCreated_ForPublicClient()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var request = new CreateOAuthAppRequest
        {
            Name = "Public App",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" },
            AllowedScopes = new[] { "read" }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var app = await response.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();
        Assert.NotNull(app);
        Assert.Equal(OAuthClientType.Public, app.ClientType);
        Assert.Null(app.ClientSecret); // Public clients don't have secrets
    }

    [Fact]
    public async Task CreateOAuthApp_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var request = new CreateOAuthAppRequest
        {
            Name = "",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOAuthApp_CreatesAuditLog()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var request = new CreateOAuthAppRequest
        {
            Name = "Audit Test App",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = new[] { "https://example.com/callback" }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var app = await response.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();
        Assert.NotNull(app);

        // Get audit logs
        var auditResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/apps/{app.Id}/audit-logs");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<PagedList<OAuthAppAuditLogDto>>();
        Assert.NotNull(auditLogs);
        Assert.Single(auditLogs.Items);
        Assert.Equal(OAuthAppAuditEventType.Created, auditLogs.Items[0].EventType);
    }

    #endregion

    #region List OAuth Apps Tests

    [Fact]
    public async Task ListOAuthApps_ReturnsPagedList()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();

        // Create multiple apps
        for (int i = 0; i < 3; i++)
        {
            var request = new CreateOAuthAppRequest
            {
                Name = $"App {i}",
                ClientType = OAuthClientType.Public,
                RedirectUris = new[] { "https://example.com/callback" }
            };
            await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", request);
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/apps");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var apps = await response.Content.ReadFromJsonAsync<PagedList<OAuthAppDto>>();
        Assert.NotNull(apps);
        Assert.Equal(3, apps.TotalCount);
        Assert.Equal(3, apps.Items.Count);
    }

    #endregion

    #region Get OAuth App Tests

    [Fact]
    public async Task GetOAuthApp_ReturnsApp_WhenExists()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var createRequest = new CreateOAuthAppRequest
        {
            Name = "Get Test App",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" }
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/apps/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var app = await response.Content.ReadFromJsonAsync<OAuthAppDto>();
        Assert.NotNull(app);
        Assert.Equal("Get Test App", app.Name);
    }

    [Fact]
    public async Task GetOAuthApp_ReturnsNotFound_WhenDoesNotExist()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();

        // Act
        var response = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/apps/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Update OAuth App Tests

    [Fact]
    public async Task UpdateOAuthApp_UpdatesName()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var createRequest = new CreateOAuthAppRequest
        {
            Name = "Original Name",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" }
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();

        var updateRequest = new UpdateOAuthAppRequest
        {
            Name = "Updated Name"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var app = await response.Content.ReadFromJsonAsync<OAuthAppDto>();
        Assert.NotNull(app);
        Assert.Equal("Updated Name", app.Name);
    }

    [Fact]
    public async Task UpdateOAuthApp_DisablesApp()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var createRequest = new CreateOAuthAppRequest
        {
            Name = "To Disable",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" }
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();

        var updateRequest = new UpdateOAuthAppRequest
        {
            IsEnabled = false
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps/{created!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var app = await response.Content.ReadFromJsonAsync<OAuthAppDto>();
        Assert.NotNull(app);
        Assert.False(app.IsEnabled);

        // Verify audit log
        var auditResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/apps/{created.Id}/audit-logs");
        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<PagedList<OAuthAppAuditLogDto>>();
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Items, l => l.EventType == OAuthAppAuditEventType.Disabled);
    }

    #endregion

    #region Delete OAuth App Tests

    [Fact]
    public async Task DeleteOAuthApp_ReturnsNoContent()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var createRequest = new CreateOAuthAppRequest
        {
            Name = "To Delete",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" }
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/workspaces/{workspaceId}/apps/{created!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify app is deleted
        var getResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/apps/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    #endregion

    #region Rotate Secret Tests

    [Fact]
    public async Task RotateSecret_ReturnsNewSecret_ForConfidentialClient()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var createRequest = new CreateOAuthAppRequest
        {
            Name = "Secret Rotation Test",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = new[] { "https://example.com/callback" }
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();
        var originalSecret = created!.ClientSecret;

        // Act
        var response = await _client.PostAsync($"/api/v1/workspaces/{workspaceId}/apps/{created.Id}/rotate-secret", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rotated = await response.Content.ReadFromJsonAsync<OAuthAppSecretRotatedDto>();
        Assert.NotNull(rotated);
        Assert.NotEqual(originalSecret, rotated.ClientSecret);
        Assert.Equal(created.ClientId, rotated.ClientId);

        // Verify audit log
        var auditResponse = await _client.GetAsync($"/api/v1/workspaces/{workspaceId}/apps/{created.Id}/audit-logs");
        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<PagedList<OAuthAppAuditLogDto>>();
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Items, l => l.EventType == OAuthAppAuditEventType.SecretRotated);
    }

    [Fact]
    public async Task RotateSecret_ReturnsBadRequest_ForPublicClient()
    {
        // Arrange
        var workspaceId = await CreateTestWorkspaceWithAdminUser();
        var createRequest = new CreateOAuthAppRequest
        {
            Name = "Public Client",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" }
        };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OAuthAppCreatedDto>();

        // Act
        var response = await _client.PostAsync($"/api/v1/workspaces/{workspaceId}/apps/{created!.Id}/rotate-secret", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Authorization Tests

    [Fact]
    public async Task OAuthAppEndpoints_ReturnForbidden_ForNonAdmin()
    {
        // Arrange
        var workspaceId = await CreateWorkspaceWithMemberOnlyAccess();
        var request = new CreateOAuthAppRequest
        {
            Name = "Test App",
            ClientType = OAuthClientType.Public,
            RedirectUris = new[] { "https://example.com/callback" }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/apps", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreateWorkspaceWithMemberOnlyAccess()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

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
            Name = "Member Only Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        // Only Member role, not Admin
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

        return workspace.Id;
    }

    #endregion
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.App.Auth;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Domain.Enums;
using Octopus.Server.Persistence.EfCore;

namespace Octopus.Server.App.Tests.Auth;

/// <summary>
/// Tests for OAuth scope enforcement in the AuthorizationService.
/// Feature: M13-005 - Enforce scope checks across all API endpoints.
/// </summary>
public class ScopeEnforcementTests : IDisposable
{
    private readonly OctopusDbContext _dbContext;
    private readonly IUserContext _userContext;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DefaultHttpContext _httpContext;
    private readonly AuthorizationService _sut;

    private readonly Guid _userId = Guid.NewGuid();

    public ScopeEnforcementTests()
    {
        var options = new DbContextOptionsBuilder<OctopusDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new OctopusDbContext(options);
        _userContext = Substitute.For<IUserContext>();
        _userContext.IsAuthenticated.Returns(true);
        _userContext.UserId.Returns(_userId);

        _workspaceContext = Substitute.For<IWorkspaceContext>();
        _workspaceContext.IsBound.Returns(false);
        _workspaceContext.WorkspaceId.Returns((Guid?)null);

        _httpContext = new DefaultHttpContext();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContextAccessor.HttpContext.Returns(_httpContext);

        _sut = new AuthorizationService(_userContext, _workspaceContext, _dbContext, _httpContextAccessor);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private void SetupScopeClaims(params string[] scopes)
    {
        var scopeString = string.Join(" ", scopes);
        var claims = new List<Claim>
        {
            new("sub", _userId.ToString()),
            new("scp", scopeString)
        };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        _httpContext.User = principal;
    }

    private void SetupNoScopes()
    {
        // Authenticated but with no scopes (e.g., OIDC without scope claims)
        var claims = new List<Claim>
        {
            new("sub", _userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        _httpContext.User = principal;
    }

    #region GetScopes Tests

    [Fact]
    public void GetScopes_ReturnsEmptySet_WhenNoHttpContext()
    {
        // Arrange
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var sut = new AuthorizationService(_userContext, _workspaceContext, _dbContext, _httpContextAccessor);

        // Act
        var scopes = sut.GetScopes();

        // Assert
        Assert.Empty(scopes);
    }

    [Fact]
    public void GetScopes_ReturnsEmptySet_WhenNoScopeClaim()
    {
        // Arrange
        SetupNoScopes();

        // Act
        var scopes = _sut.GetScopes();

        // Assert
        Assert.Empty(scopes);
    }

    [Fact]
    public void GetScopes_ReturnsParsedScopes()
    {
        // Arrange
        SetupScopeClaims("files:read", "models:read", "workspaces:read");

        // Act
        var scopes = _sut.GetScopes();

        // Assert
        Assert.Equal(3, scopes.Count);
        Assert.Contains("files:read", scopes);
        Assert.Contains("models:read", scopes);
        Assert.Contains("workspaces:read", scopes);
    }

    [Fact]
    public void GetScopes_CachesResult()
    {
        // Arrange
        SetupScopeClaims("files:read");

        // Act
        var scopes1 = _sut.GetScopes();
        var scopes2 = _sut.GetScopes();

        // Assert - Same instance should be returned
        Assert.Same(scopes1, scopes2);
    }

    #endregion

    #region HasScope Tests

    [Fact]
    public void HasScope_ReturnsFalse_WhenScopeNotPresent()
    {
        // Arrange
        SetupScopeClaims("files:read");

        // Act
        var result = _sut.HasScope("models:read");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasScope_ReturnsTrue_WhenScopePresent()
    {
        // Arrange
        SetupScopeClaims("files:read", "models:read");

        // Act
        var result = _sut.HasScope("models:read");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region HasAnyScope Tests

    [Fact]
    public void HasAnyScope_ReturnsFalse_WhenNoMatchingScope()
    {
        // Arrange
        SetupScopeClaims("files:read");

        // Act
        var result = _sut.HasAnyScope("models:read", "projects:read");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasAnyScope_ReturnsTrue_WhenAtLeastOneScopeMatches()
    {
        // Arrange
        SetupScopeClaims("files:read", "projects:read");

        // Act
        var result = _sut.HasAnyScope("models:read", "projects:read");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region HasAllScopes Tests

    [Fact]
    public void HasAllScopes_ReturnsFalse_WhenSomeScopesMissing()
    {
        // Arrange
        SetupScopeClaims("files:read");

        // Act
        var result = _sut.HasAllScopes("files:read", "models:read");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasAllScopes_ReturnsTrue_WhenAllScopesPresent()
    {
        // Arrange
        SetupScopeClaims("files:read", "models:read", "projects:read");

        // Act
        var result = _sut.HasAllScopes("files:read", "models:read");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region RequireScope Tests

    [Fact]
    public void RequireScope_ThrowsUnauthorized_WhenNotAuthenticated()
    {
        // Arrange
        _userContext.IsAuthenticated.Returns(false);
        SetupScopeClaims("files:read");

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => _sut.RequireScope("files:read"));
    }

    [Fact]
    public void RequireScope_Succeeds_WhenNoScopesInToken()
    {
        // Arrange - Backward compatibility: if no scopes in token, allow access
        SetupNoScopes();

        // Act - Should not throw
        _sut.RequireScope("files:read");
    }

    [Fact]
    public void RequireScope_ThrowsInsufficientScope_WhenScopeMissing()
    {
        // Arrange
        SetupScopeClaims("files:read");

        // Act & Assert
        var ex = Assert.Throws<InsufficientScopeException>(() => _sut.RequireScope("models:read"));
        Assert.Contains("models:read", ex.RequiredScopes);
        Assert.Contains("files:read", ex.PresentScopes);
    }

    [Fact]
    public void RequireScope_Succeeds_WhenAnyScopeMatches()
    {
        // Arrange
        SetupScopeClaims("files:read", "models:read");

        // Act - Should not throw
        _sut.RequireScope("files:read", "projects:read");
    }

    [Fact]
    public void RequireScope_ThrowsInsufficientScope_WhenNoScopesMatch()
    {
        // Arrange
        SetupScopeClaims("files:read");

        // Act & Assert
        var ex = Assert.Throws<InsufficientScopeException>(() =>
            _sut.RequireScope("models:read", "projects:read"));
        Assert.Contains("models:read", ex.RequiredScopes);
        Assert.Contains("projects:read", ex.RequiredScopes);
    }

    #endregion

    #region RequireAllScopes Tests

    [Fact]
    public void RequireAllScopes_ThrowsUnauthorized_WhenNotAuthenticated()
    {
        // Arrange
        _userContext.IsAuthenticated.Returns(false);
        SetupScopeClaims("files:read", "models:read");

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() =>
            _sut.RequireAllScopes("files:read", "models:read"));
    }

    [Fact]
    public void RequireAllScopes_Succeeds_WhenNoScopesInToken()
    {
        // Arrange - Backward compatibility
        SetupNoScopes();

        // Act - Should not throw
        _sut.RequireAllScopes("files:read", "models:read");
    }

    [Fact]
    public void RequireAllScopes_ThrowsInsufficientScope_WhenAnyScopeMissing()
    {
        // Arrange
        SetupScopeClaims("files:read");

        // Act & Assert
        var ex = Assert.Throws<InsufficientScopeException>(() =>
            _sut.RequireAllScopes("files:read", "models:read"));
        Assert.Contains("files:read", ex.RequiredScopes);
        Assert.Contains("models:read", ex.RequiredScopes);
    }

    [Fact]
    public void RequireAllScopes_Succeeds_WhenAllScopesPresent()
    {
        // Arrange
        SetupScopeClaims("files:read", "models:read", "projects:read");

        // Act - Should not throw
        _sut.RequireAllScopes("files:read", "models:read");
    }

    #endregion

    #region Scope Constants Tests

    [Fact]
    public void OAuthScopes_DefinesExpectedScopes()
    {
        // Assert - All expected scopes are defined
        Assert.Equal("workspaces:read", OAuthScopes.WorkspacesRead);
        Assert.Equal("workspaces:write", OAuthScopes.WorkspacesWrite);
        Assert.Equal("projects:read", OAuthScopes.ProjectsRead);
        Assert.Equal("projects:write", OAuthScopes.ProjectsWrite);
        Assert.Equal("files:read", OAuthScopes.FilesRead);
        Assert.Equal("files:write", OAuthScopes.FilesWrite);
        Assert.Equal("models:read", OAuthScopes.ModelsRead);
        Assert.Equal("models:write", OAuthScopes.ModelsWrite);
        Assert.Equal("processing:read", OAuthScopes.ProcessingRead);
        Assert.Equal("processing:write", OAuthScopes.ProcessingWrite);
        Assert.Equal("oauth_apps:read", OAuthScopes.OAuthAppsRead);
        Assert.Equal("oauth_apps:write", OAuthScopes.OAuthAppsWrite);
        Assert.Equal("oauth_apps:admin", OAuthScopes.OAuthAppsAdmin);
        Assert.Equal("pats:read", OAuthScopes.PatsRead);
        Assert.Equal("pats:write", OAuthScopes.PatsWrite);
        Assert.Equal("pats:admin", OAuthScopes.PatsAdmin);
    }

    [Fact]
    public void OAuthScopes_AllScopes_ContainsAllDefinedScopes()
    {
        // Assert - AllScopes contains all defined scopes
        Assert.Equal(16, OAuthScopes.AllScopes.Count);
        Assert.Contains(OAuthScopes.WorkspacesRead, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.WorkspacesWrite, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.ProjectsRead, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.ProjectsWrite, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.FilesRead, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.FilesWrite, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.ModelsRead, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.ModelsWrite, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.ProcessingRead, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.ProcessingWrite, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.OAuthAppsRead, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.OAuthAppsWrite, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.OAuthAppsAdmin, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.PatsRead, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.PatsWrite, OAuthScopes.AllScopes);
        Assert.Contains(OAuthScopes.PatsAdmin, OAuthScopes.AllScopes);
    }

    [Fact]
    public void OAuthScopes_AreValidScopes_ReturnsTrue_ForValidScopes()
    {
        // Arrange
        var scopes = new[] { "files:read", "models:read" };

        // Act
        var result = OAuthScopes.AreValidScopes(scopes);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void OAuthScopes_AreValidScopes_ReturnsFalse_ForInvalidScopes()
    {
        // Arrange
        var scopes = new[] { "files:read", "invalid:scope" };

        // Act
        var result = OAuthScopes.AreValidScopes(scopes);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void OAuthScopes_GetInvalidScopes_ReturnsInvalidScopes()
    {
        // Arrange
        var scopes = new[] { "files:read", "invalid:scope", "another:invalid" };

        // Act
        var invalidScopes = OAuthScopes.GetInvalidScopes(scopes);

        // Assert
        Assert.Equal(2, invalidScopes.Count);
        Assert.Contains("invalid:scope", invalidScopes);
        Assert.Contains("another:invalid", invalidScopes);
    }

    #endregion

    #region Combined Scope and RBAC Tests

    [Fact]
    public async Task Authorization_RequiresBothScopeAndRbac()
    {
        // Arrange - User has workspace membership
        var workspaceId = Guid.NewGuid();
        _dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Setup scopes - user has files:read but NOT workspaces:read
        SetupScopeClaims("files:read");

        // Act - RBAC check should pass
        var canAccessWorkspace = await _sut.CanAccessWorkspaceAsync(workspaceId, WorkspaceRole.Member);
        Assert.True(canAccessWorkspace);

        // Act - Scope check should fail for workspaces:read
        var ex = Assert.Throws<InsufficientScopeException>(() =>
            _sut.RequireScope(OAuthScopes.WorkspacesRead));
        Assert.Contains("workspaces:read", ex.RequiredScopes);
    }

    [Fact]
    public async Task Authorization_PassesBothScopeAndRbac()
    {
        // Arrange - User has workspace membership
        var workspaceId = Guid.NewGuid();
        _dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Setup scopes - user has workspaces:read
        SetupScopeClaims("workspaces:read");

        // Act - Both RBAC and scope checks should pass
        var canAccessWorkspace = await _sut.CanAccessWorkspaceAsync(workspaceId, WorkspaceRole.Member);
        Assert.True(canAccessWorkspace);

        // Scope check should also pass
        _sut.RequireScope(OAuthScopes.WorkspacesRead); // Should not throw
    }

    #endregion
}

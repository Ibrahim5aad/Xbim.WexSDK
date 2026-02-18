using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.App.Auth;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Domain.Enums;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.App.Tests.Auth;

/// <summary>
/// Tests for workspace isolation enforcement (multi-tenant isolation).
/// Feature: M13-006 - Bind all API requests to a workspace context to enforce multi-tenant isolation.
/// </summary>
public class WorkspaceIsolationTests : IDisposable
{
    private readonly XbimDbContext _dbContext;
    private readonly IUserContext _userContext;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DefaultHttpContext _httpContext;
    private readonly AuthorizationService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _workspaceAId = Guid.NewGuid();
    private readonly Guid _workspaceBId = Guid.NewGuid();
    private readonly Guid _projectInAId = Guid.NewGuid();
    private readonly Guid _projectInBId = Guid.NewGuid();

    public WorkspaceIsolationTests()
    {
        var options = new DbContextOptionsBuilder<XbimDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new XbimDbContext(options);
        _userContext = Substitute.For<IUserContext>();
        _userContext.IsAuthenticated.Returns(true);
        _userContext.UserId.Returns(_userId);

        _workspaceContext = Substitute.For<IWorkspaceContext>();

        _httpContext = new DefaultHttpContext();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContextAccessor.HttpContext.Returns(_httpContext);

        _sut = new AuthorizationService(_userContext, _workspaceContext, _dbContext, _httpContextAccessor);

        SeedTestData();
    }

    private void SeedTestData()
    {
        // Create user
        var user = new User
        {
            Id = _userId,
            Subject = "test-user",
            Email = "test@example.com",
            DisplayName = "Test User",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Create two workspaces
        var workspaceA = new Workspace
        {
            Id = _workspaceAId,
            Name = "Workspace A",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var workspaceB = new Workspace
        {
            Id = _workspaceBId,
            Name = "Workspace B",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Create projects in each workspace
        var projectInA = new Project
        {
            Id = _projectInAId,
            WorkspaceId = _workspaceAId,
            Name = "Project in Workspace A",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var projectInB = new Project
        {
            Id = _projectInBId,
            WorkspaceId = _workspaceBId,
            Name = "Project in Workspace B",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // User is member of both workspaces
        var membershipA = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceAId,
            UserId = _userId,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var membershipB = new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceBId,
            UserId = _userId,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Users.Add(user);
        _dbContext.Workspaces.AddRange(workspaceA, workspaceB);
        _dbContext.Projects.AddRange(projectInA, projectInB);
        _dbContext.WorkspaceMemberships.AddRange(membershipA, membershipB);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetBoundWorkspaceId Tests

    [Fact]
    public void GetBoundWorkspaceId_ReturnsNull_WhenNoWorkspaceContextIsBound()
    {
        // Arrange - workspace context not bound (dev mode)
        _workspaceContext.IsBound.Returns(false);
        _workspaceContext.WorkspaceId.Returns((Guid?)null);

        // Act
        var result = _sut.GetBoundWorkspaceId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetBoundWorkspaceId_ReturnsWorkspaceId_WhenBound()
    {
        // Arrange - workspace context is bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act
        var result = _sut.GetBoundWorkspaceId();

        // Assert
        Assert.Equal(_workspaceAId, result);
    }

    #endregion

    #region ValidateWorkspaceIsolation Tests

    [Fact]
    public void ValidateWorkspaceIsolation_ReturnsTrue_WhenNoContextBound()
    {
        // Arrange - no workspace context (dev mode)
        _workspaceContext.IsBound.Returns(false);

        // Act - try to access workspace A (should be allowed since no isolation enforced)
        var result = _sut.ValidateWorkspaceIsolation(_workspaceAId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateWorkspaceIsolation_ReturnsTrue_WhenWorkspaceMatches()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act - try to access workspace A (should be allowed)
        var result = _sut.ValidateWorkspaceIsolation(_workspaceAId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateWorkspaceIsolation_ReturnsFalse_WhenWorkspaceMismatch()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act - try to access workspace B (should be denied)
        var result = _sut.ValidateWorkspaceIsolation(_workspaceBId);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RequireWorkspaceIsolation Tests

    [Fact]
    public void RequireWorkspaceIsolation_DoesNotThrow_WhenNoContextBound()
    {
        // Arrange - no workspace context (dev mode)
        _workspaceContext.IsBound.Returns(false);

        // Act & Assert - should not throw (no isolation enforced)
        var exception = Record.Exception(() => _sut.RequireWorkspaceIsolation(_workspaceBId));
        Assert.Null(exception);
    }

    [Fact]
    public void RequireWorkspaceIsolation_DoesNotThrow_WhenWorkspaceMatches()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act & Assert - should not throw (workspace matches)
        var exception = Record.Exception(() => _sut.RequireWorkspaceIsolation(_workspaceAId));
        Assert.Null(exception);
    }

    [Fact]
    public void RequireWorkspaceIsolation_ThrowsWorkspaceIsolationException_WhenWorkspaceMismatch()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act - try to access workspace B
        var exception = Assert.Throws<WorkspaceIsolationException>(
            () => _sut.RequireWorkspaceIsolation(_workspaceBId));

        // Assert - exception contains correct workspace IDs
        Assert.Equal(_workspaceAId, exception.TokenWorkspaceId);
        Assert.Equal(_workspaceBId, exception.ResourceWorkspaceId);
        Assert.Contains("Cross-workspace access denied", exception.Message);
    }

    #endregion

    #region ValidateProjectWorkspaceIsolationAsync Tests

    [Fact]
    public async Task ValidateProjectWorkspaceIsolationAsync_ReturnsTrue_WhenNoContextBound()
    {
        // Arrange - no workspace context (dev mode)
        _workspaceContext.IsBound.Returns(false);

        // Act - try to access project in workspace B (should be allowed since no isolation enforced)
        var result = await _sut.ValidateProjectWorkspaceIsolationAsync(_projectInBId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateProjectWorkspaceIsolationAsync_ReturnsTrue_WhenProjectInBoundWorkspace()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act - try to access project in workspace A (should be allowed)
        var result = await _sut.ValidateProjectWorkspaceIsolationAsync(_projectInAId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateProjectWorkspaceIsolationAsync_ReturnsFalse_WhenProjectInDifferentWorkspace()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act - try to access project in workspace B (should be denied)
        var result = await _sut.ValidateProjectWorkspaceIsolationAsync(_projectInBId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateProjectWorkspaceIsolationAsync_ReturnsTrue_WhenProjectDoesNotExist()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act - try to access non-existent project (returns true to let endpoint handle 404)
        var result = await _sut.ValidateProjectWorkspaceIsolationAsync(Guid.NewGuid());

        // Assert
        Assert.True(result);
    }

    #endregion

    #region RequireProjectWorkspaceIsolationAsync Tests

    [Fact]
    public async Task RequireProjectWorkspaceIsolationAsync_DoesNotThrow_WhenNoContextBound()
    {
        // Arrange - no workspace context (dev mode)
        _workspaceContext.IsBound.Returns(false);

        // Act & Assert - should not throw (no isolation enforced)
        var exception = await Record.ExceptionAsync(
            () => _sut.RequireProjectWorkspaceIsolationAsync(_projectInBId));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RequireProjectWorkspaceIsolationAsync_DoesNotThrow_WhenProjectInBoundWorkspace()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act & Assert - should not throw (project is in correct workspace)
        var exception = await Record.ExceptionAsync(
            () => _sut.RequireProjectWorkspaceIsolationAsync(_projectInAId));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RequireProjectWorkspaceIsolationAsync_ThrowsWorkspaceIsolationException_WhenProjectInDifferentWorkspace()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act - try to access project in workspace B
        var exception = await Assert.ThrowsAsync<WorkspaceIsolationException>(
            () => _sut.RequireProjectWorkspaceIsolationAsync(_projectInBId));

        // Assert - exception contains correct workspace IDs
        Assert.Equal(_workspaceAId, exception.TokenWorkspaceId);
        Assert.Equal(_workspaceBId, exception.ResourceWorkspaceId);
    }

    [Fact]
    public async Task RequireProjectWorkspaceIsolationAsync_DoesNotThrow_WhenProjectDoesNotExist()
    {
        // Arrange - bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Act & Assert - should not throw for non-existent project (let endpoint handle 404)
        var exception = await Record.ExceptionAsync(
            () => _sut.RequireProjectWorkspaceIsolationAsync(Guid.NewGuid()));
        Assert.Null(exception);
    }

    #endregion

    #region Cross-Workspace Access Scenarios

    [Fact]
    public async Task CrossWorkspaceAccess_DeniedEvenWithValidMembership()
    {
        // This test confirms that even though the user is a member of BOTH workspaces,
        // when their token is bound to workspace A, they cannot access workspace B resources.

        // Arrange - user is a member of both workspaces, but token is bound to workspace A
        _workspaceContext.IsBound.Returns(true);
        _workspaceContext.WorkspaceId.Returns(_workspaceAId);

        // Verify user has valid RBAC membership in workspace B
        var hasWorkspaceBMembership = await _sut.CanAccessWorkspaceAsync(_workspaceBId);

        // User IS a member of workspace B (RBAC would allow access)
        Assert.True(hasWorkspaceBMembership);

        // But workspace isolation STILL denies access
        Assert.Throws<WorkspaceIsolationException>(
            () => _sut.RequireWorkspaceIsolation(_workspaceBId));

        // And project access in workspace B is also denied
        await Assert.ThrowsAsync<WorkspaceIsolationException>(
            () => _sut.RequireProjectWorkspaceIsolationAsync(_projectInBId));
    }

    #endregion
}

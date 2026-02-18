using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.App.Auth;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Domain.Enums;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.App.Tests.Auth;

public class AuthorizationServiceTests : IDisposable
{
    private readonly XbimDbContext _dbContext;
    private readonly IUserContext _userContext;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthorizationService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();

    public AuthorizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<XbimDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new XbimDbContext(options);
        _userContext = Substitute.For<IUserContext>();
        _userContext.IsAuthenticated.Returns(true);
        _userContext.UserId.Returns(_userId);

        _workspaceContext = Substitute.For<IWorkspaceContext>();
        _workspaceContext.IsBound.Returns(false);
        _workspaceContext.WorkspaceId.Returns((Guid?)null);

        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        _sut = new AuthorizationService(_userContext, _workspaceContext, _dbContext, _httpContextAccessor);

        // Seed base data
        SeedTestData();
    }

    private void SeedTestData()
    {
        var user = new User
        {
            Id = _userId,
            Subject = "test-user",
            Email = "test@example.com",
            DisplayName = "Test User",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var otherUser = new User
        {
            Id = _otherUserId,
            Subject = "other-user",
            Email = "other@example.com",
            DisplayName = "Other User",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var workspace = new Workspace
        {
            Id = _workspaceId,
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var project = new Project
        {
            Id = _projectId,
            WorkspaceId = _workspaceId,
            Name = "Test Project",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Users.AddRange(user, otherUser);
        _dbContext.Workspaces.Add(workspace);
        _dbContext.Projects.Add(project);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetWorkspaceRoleAsync Tests

    [Fact]
    public async Task GetWorkspaceRoleAsync_ReturnsNull_WhenNotAuthenticated()
    {
        // Arrange
        _userContext.IsAuthenticated.Returns(false);

        // Act
        var result = await _sut.GetWorkspaceRoleAsync(_workspaceId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetWorkspaceRoleAsync_ReturnsNull_WhenNoMembership()
    {
        // Act
        var result = await _sut.GetWorkspaceRoleAsync(_workspaceId);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(WorkspaceRole.Guest)]
    [InlineData(WorkspaceRole.Member)]
    [InlineData(WorkspaceRole.Admin)]
    [InlineData(WorkspaceRole.Owner)]
    public async Task GetWorkspaceRoleAsync_ReturnsCorrectRole_WhenMembershipExists(WorkspaceRole role)
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetWorkspaceRoleAsync(_workspaceId);

        // Assert
        Assert.Equal(role, result);
    }

    #endregion

    #region CanAccessWorkspaceAsync Tests

    [Fact]
    public async Task CanAccessWorkspaceAsync_ReturnsFalse_WhenNoMembership()
    {
        // Act
        var result = await _sut.CanAccessWorkspaceAsync(_workspaceId);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(WorkspaceRole.Guest, WorkspaceRole.Guest, true)]
    [InlineData(WorkspaceRole.Guest, WorkspaceRole.Member, false)]
    [InlineData(WorkspaceRole.Member, WorkspaceRole.Guest, true)]
    [InlineData(WorkspaceRole.Member, WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Member, WorkspaceRole.Admin, false)]
    [InlineData(WorkspaceRole.Admin, WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Admin, WorkspaceRole.Owner, false)]
    [InlineData(WorkspaceRole.Owner, WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Owner, WorkspaceRole.Owner, true)]
    public async Task CanAccessWorkspaceAsync_RespectsRoleHierarchy(
        WorkspaceRole actualRole,
        WorkspaceRole requiredRole,
        bool expectedResult)
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = actualRole,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.CanAccessWorkspaceAsync(_workspaceId, requiredRole);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    #endregion

    #region RequireWorkspaceAccessAsync Tests

    [Fact]
    public async Task RequireWorkspaceAccessAsync_ThrowsUnauthorized_WhenNotAuthenticated()
    {
        // Arrange
        _userContext.IsAuthenticated.Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.RequireWorkspaceAccessAsync(_workspaceId));
    }

    [Fact]
    public async Task RequireWorkspaceAccessAsync_ThrowsForbidden_WhenInsufficientRole()
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => _sut.RequireWorkspaceAccessAsync(_workspaceId, WorkspaceRole.Admin));
    }

    [Fact]
    public async Task RequireWorkspaceAccessAsync_Succeeds_WhenSufficientRole()
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act - should not throw
        await _sut.RequireWorkspaceAccessAsync(_workspaceId, WorkspaceRole.Member);
    }

    #endregion

    #region GetProjectRoleAsync Tests

    [Fact]
    public async Task GetProjectRoleAsync_ReturnsNull_WhenNotAuthenticated()
    {
        // Arrange
        _userContext.IsAuthenticated.Returns(false);

        // Act
        var result = await _sut.GetProjectRoleAsync(_projectId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetProjectRoleAsync_ReturnsNull_WhenNoMembershipAndNoWorkspaceAccess()
    {
        // Act
        var result = await _sut.GetProjectRoleAsync(_projectId);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(ProjectRole.Viewer)]
    [InlineData(ProjectRole.Editor)]
    [InlineData(ProjectRole.ProjectAdmin)]
    public async Task GetProjectRoleAsync_ReturnsDirectProjectRole_WhenMembershipExists(ProjectRole role)
    {
        // Arrange
        _dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            UserId = _userId,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetProjectRoleAsync(_projectId);

        // Assert
        Assert.Equal(role, result);
    }

    [Theory]
    [InlineData(WorkspaceRole.Admin)]
    [InlineData(WorkspaceRole.Owner)]
    public async Task GetProjectRoleAsync_ReturnsProjectAdmin_ForWorkspaceAdminOrOwner(WorkspaceRole workspaceRole)
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = workspaceRole,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetProjectRoleAsync(_projectId);

        // Assert
        Assert.Equal(ProjectRole.ProjectAdmin, result);
    }

    [Fact]
    public async Task GetProjectRoleAsync_ReturnsViewer_ForWorkspaceMember()
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Member,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetProjectRoleAsync(_projectId);

        // Assert
        Assert.Equal(ProjectRole.Viewer, result);
    }

    [Fact]
    public async Task GetProjectRoleAsync_ReturnsNull_ForWorkspaceGuest()
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Guest,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetProjectRoleAsync(_projectId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetProjectRoleAsync_PrefersDirectProjectMembership_OverWorkspaceRole()
    {
        // Arrange - User is workspace guest but project editor
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Guest,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            UserId = _userId,
            Role = ProjectRole.Editor,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetProjectRoleAsync(_projectId);

        // Assert - Should return direct project role, not workspace-implied role
        Assert.Equal(ProjectRole.Editor, result);
    }

    #endregion

    #region CanAccessProjectAsync Tests

    [Fact]
    public async Task CanAccessProjectAsync_ReturnsFalse_WhenNoAccess()
    {
        // Act
        var result = await _sut.CanAccessProjectAsync(_projectId);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(ProjectRole.Viewer, ProjectRole.Viewer, true)]
    [InlineData(ProjectRole.Viewer, ProjectRole.Editor, false)]
    [InlineData(ProjectRole.Editor, ProjectRole.Viewer, true)]
    [InlineData(ProjectRole.Editor, ProjectRole.Editor, true)]
    [InlineData(ProjectRole.Editor, ProjectRole.ProjectAdmin, false)]
    [InlineData(ProjectRole.ProjectAdmin, ProjectRole.Editor, true)]
    [InlineData(ProjectRole.ProjectAdmin, ProjectRole.ProjectAdmin, true)]
    public async Task CanAccessProjectAsync_RespectsRoleHierarchy(
        ProjectRole actualRole,
        ProjectRole requiredRole,
        bool expectedResult)
    {
        // Arrange
        _dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            UserId = _userId,
            Role = actualRole,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.CanAccessProjectAsync(_projectId, requiredRole);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task CanAccessProjectAsync_GrantsAccess_ViaWorkspaceAdmin()
    {
        // Arrange
        _dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = WorkspaceRole.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.CanAccessProjectAsync(_projectId, ProjectRole.ProjectAdmin);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region RequireProjectAccessAsync Tests

    [Fact]
    public async Task RequireProjectAccessAsync_ThrowsUnauthorized_WhenNotAuthenticated()
    {
        // Arrange
        _userContext.IsAuthenticated.Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.RequireProjectAccessAsync(_projectId));
    }

    [Fact]
    public async Task RequireProjectAccessAsync_ThrowsForbidden_WhenInsufficientRole()
    {
        // Arrange
        _dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            UserId = _userId,
            Role = ProjectRole.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => _sut.RequireProjectAccessAsync(_projectId, ProjectRole.Editor));
    }

    [Fact]
    public async Task RequireProjectAccessAsync_Succeeds_WhenSufficientRole()
    {
        // Arrange
        _dbContext.ProjectMemberships.Add(new ProjectMembership
        {
            Id = Guid.NewGuid(),
            ProjectId = _projectId,
            UserId = _userId,
            Role = ProjectRole.Editor,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act - should not throw
        await _sut.RequireProjectAccessAsync(_projectId, ProjectRole.Viewer);
    }

    #endregion
}

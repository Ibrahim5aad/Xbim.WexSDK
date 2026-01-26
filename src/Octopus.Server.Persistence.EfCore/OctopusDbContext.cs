using Microsoft.EntityFrameworkCore;
using Octopus.Server.Domain.Entities;

namespace Octopus.Server.Persistence.EfCore;

/// <summary>
/// The main database context for the Octopus platform.
/// </summary>
public class OctopusDbContext : DbContext
{
    public OctopusDbContext(DbContextOptions<OctopusDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();
    public DbSet<WorkspaceInvite> WorkspaceInvites => Set<WorkspaceInvite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OctopusDbContext).Assembly);
    }
}

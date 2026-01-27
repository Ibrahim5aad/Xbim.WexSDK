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
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<FileLink> FileLinks => Set<FileLink>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<Model> Models => Set<Model>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    // OAuth Apps
    public DbSet<OAuthApp> OAuthApps => Set<OAuthApp>();
    public DbSet<OAuthAppAuditLog> OAuthAppAuditLogs => Set<OAuthAppAuditLog>();

    // IFC Properties
    public DbSet<IfcElement> IfcElements => Set<IfcElement>();
    public DbSet<IfcPropertySet> IfcPropertySets => Set<IfcPropertySet>();
    public DbSet<IfcProperty> IfcProperties => Set<IfcProperty>();
    public DbSet<IfcQuantitySet> IfcQuantitySets => Set<IfcQuantitySet>();
    public DbSet<IfcQuantity> IfcQuantities => Set<IfcQuantity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OctopusDbContext).Assembly);
    }
}

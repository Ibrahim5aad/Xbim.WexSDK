using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.WorkspaceId)
            .IsRequired();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // One project has many memberships
        builder.HasMany(p => p.Memberships)
            .WithOne(m => m.Project)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for faster lookups by workspace
        builder.HasIndex(p => p.WorkspaceId);

        // One project has many files
        builder.HasMany(p => p.Files)
            .WithOne(f => f.Project)
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // One project has many upload sessions
        builder.HasMany(p => p.UploadSessions)
            .WithOne(us => us.Project)
            .HasForeignKey(us => us.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // One project has many models
        builder.HasMany(p => p.Models)
            .WithOne(m => m.Project)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

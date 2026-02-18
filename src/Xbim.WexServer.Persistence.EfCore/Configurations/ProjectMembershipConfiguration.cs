using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class ProjectMembershipConfiguration : IEntityTypeConfiguration<ProjectMembership>
{
    public void Configure(EntityTypeBuilder<ProjectMembership> builder)
    {
        builder.ToTable("ProjectMemberships");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ProjectId)
            .IsRequired();

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.Property(m => m.Role)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        // User relationship
        builder.HasOne(m => m.User)
            .WithMany(u => u.ProjectMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint: one membership per user per project
        builder.HasIndex(m => new { m.ProjectId, m.UserId })
            .IsUnique();

        // Index for user lookups
        builder.HasIndex(m => m.UserId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Octopus.Server.Domain.Entities;

namespace Octopus.Server.Persistence.EfCore.Configurations;

public class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("WorkspaceMemberships");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.WorkspaceId)
            .IsRequired();

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.Property(m => m.Role)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        // User relationship
        builder.HasOne(m => m.User)
            .WithMany(u => u.WorkspaceMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint: one membership per user per workspace
        builder.HasIndex(m => new { m.WorkspaceId, m.UserId })
            .IsUnique();

        // Index for user lookups
        builder.HasIndex(m => m.UserId);
    }
}

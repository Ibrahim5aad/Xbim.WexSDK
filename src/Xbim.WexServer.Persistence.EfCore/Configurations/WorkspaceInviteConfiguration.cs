using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class WorkspaceInviteConfiguration : IEntityTypeConfiguration<WorkspaceInvite>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvite> builder)
    {
        builder.ToTable("WorkspaceInvites");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.WorkspaceId)
            .IsRequired();

        builder.Property(i => i.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Role)
            .IsRequired();

        builder.Property(i => i.Token)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(i => i.CreatedAt)
            .IsRequired();

        builder.Property(i => i.ExpiresAt)
            .IsRequired();

        // Accepted by user relationship
        builder.HasOne(i => i.AcceptedByUser)
            .WithMany()
            .HasForeignKey(i => i.AcceptedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique constraint on token
        builder.HasIndex(i => i.Token)
            .IsUnique();

        // Index for workspace lookups
        builder.HasIndex(i => i.WorkspaceId);

        // Index for email lookups
        builder.HasIndex(i => i.Email);
    }
}

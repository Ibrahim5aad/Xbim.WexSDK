using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class OAuthAppAuditLogConfiguration : IEntityTypeConfiguration<OAuthAppAuditLog>
{
    public void Configure(EntityTypeBuilder<OAuthAppAuditLog> builder)
    {
        builder.ToTable("OAuthAppAuditLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.OAuthAppId)
            .IsRequired();

        builder.Property(l => l.EventType)
            .IsRequired();

        builder.Property(l => l.ActorUserId)
            .IsRequired();

        builder.Property(l => l.Timestamp)
            .IsRequired();

        builder.Property(l => l.Details)
            .HasMaxLength(4000);

        builder.Property(l => l.IpAddress)
            .HasMaxLength(45); // IPv6 max length

        // Index for querying by app
        builder.HasIndex(l => l.OAuthAppId);

        // Index for querying by timestamp
        builder.HasIndex(l => l.Timestamp);

        // Actor relationship
        builder.HasOne(l => l.ActorUser)
            .WithMany()
            .HasForeignKey(l => l.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

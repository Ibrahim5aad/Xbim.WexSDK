using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Octopus.Server.Domain.Entities;

namespace Octopus.Server.Persistence.EfCore.Configurations;

public class PersonalAccessTokenAuditLogConfiguration : IEntityTypeConfiguration<PersonalAccessTokenAuditLog>
{
    public void Configure(EntityTypeBuilder<PersonalAccessTokenAuditLog> builder)
    {
        builder.ToTable("PersonalAccessTokenAuditLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.PersonalAccessTokenId)
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

        builder.Property(l => l.UserAgent)
            .HasMaxLength(500);

        // Index for querying by PAT
        builder.HasIndex(l => l.PersonalAccessTokenId);

        // Index for querying by timestamp
        builder.HasIndex(l => l.Timestamp);

        // PAT relationship
        builder.HasOne(l => l.PersonalAccessToken)
            .WithMany()
            .HasForeignKey(l => l.PersonalAccessTokenId)
            .OnDelete(DeleteBehavior.Cascade);

        // Actor relationship
        builder.HasOne(l => l.ActorUser)
            .WithMany()
            .HasForeignKey(l => l.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> builder)
    {
        builder.ToTable("PersonalAccessTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(t => t.TokenPrefix)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.WorkspaceId)
            .IsRequired();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description)
            .HasMaxLength(1000);

        builder.Property(t => t.Scopes)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        builder.Property(t => t.IsRevoked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.RevokedReason)
            .HasMaxLength(200);

        builder.Property(t => t.CreatedFromIpAddress)
            .HasMaxLength(45); // IPv6 max length

        builder.Property(t => t.LastUsedIpAddress)
            .HasMaxLength(45);

        // Ignore computed property
        builder.Ignore(t => t.IsActive);

        // Index for quick lookup when validating token
        builder.HasIndex(t => t.TokenHash);

        // Index for cleanup of expired tokens
        builder.HasIndex(t => t.ExpiresAt);

        // Index for user lookups (list user's tokens)
        builder.HasIndex(t => t.UserId);

        // Index for workspace lookups (list workspace tokens for admin)
        builder.HasIndex(t => t.WorkspaceId);

        // Composite index for active tokens per user/workspace
        builder.HasIndex(t => new { t.UserId, t.WorkspaceId, t.IsRevoked });

        // User relationship
        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Workspace relationship
        builder.HasOne(t => t.Workspace)
            .WithMany()
            .HasForeignKey(t => t.WorkspaceId)
            .OnDelete(DeleteBehavior.NoAction); // Avoid multiple cascade paths
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Octopus.Server.Domain.Entities;

namespace Octopus.Server.Persistence.EfCore.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(t => t.OAuthAppId)
            .IsRequired();

        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.WorkspaceId)
            .IsRequired();

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

        builder.Property(t => t.TokenFamilyId)
            .IsRequired();

        builder.Property(t => t.IpAddress)
            .HasMaxLength(45); // IPv6 max length

        builder.Property(t => t.UserAgent)
            .HasMaxLength(500);

        // Index for quick lookup when validating token
        builder.HasIndex(t => t.TokenHash);

        // Index for token family (for detecting reuse)
        builder.HasIndex(t => t.TokenFamilyId);

        // Index for cleanup of expired tokens
        builder.HasIndex(t => t.ExpiresAt);

        // Index for OAuth app lookups
        builder.HasIndex(t => t.OAuthAppId);

        // Index for user lookups (revocation by user)
        builder.HasIndex(t => t.UserId);

        // Index for workspace lookups
        builder.HasIndex(t => t.WorkspaceId);

        // Composite index for active tokens per app/user
        builder.HasIndex(t => new { t.OAuthAppId, t.UserId, t.IsRevoked });

        // OAuth App relationship
        builder.HasOne(t => t.OAuthApp)
            .WithMany()
            .HasForeignKey(t => t.OAuthAppId)
            .OnDelete(DeleteBehavior.Cascade);

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

        // Self-referencing relationship for parent token (rotation chain)
        builder.HasOne(t => t.ParentToken)
            .WithOne()
            .HasForeignKey<RefreshToken>(t => t.ParentTokenId)
            .OnDelete(DeleteBehavior.NoAction);

        // Self-referencing relationship for replaced-by token
        builder.HasOne(t => t.ReplacedByToken)
            .WithOne()
            .HasForeignKey<RefreshToken>(t => t.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

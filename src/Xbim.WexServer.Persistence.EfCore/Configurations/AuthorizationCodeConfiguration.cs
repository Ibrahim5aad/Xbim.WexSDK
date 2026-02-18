using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class AuthorizationCodeConfiguration : IEntityTypeConfiguration<AuthorizationCode>
{
    public void Configure(EntityTypeBuilder<AuthorizationCode> builder)
    {
        builder.ToTable("AuthorizationCodes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CodeHash)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(c => c.OAuthAppId)
            .IsRequired();

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.Property(c => c.WorkspaceId)
            .IsRequired();

        builder.Property(c => c.Scopes)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(c => c.RedirectUri)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(c => c.CodeChallenge)
            .HasMaxLength(512);

        builder.Property(c => c.CodeChallengeMethod)
            .HasMaxLength(10);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.ExpiresAt)
            .IsRequired();

        builder.Property(c => c.IsUsed)
            .IsRequired()
            .HasDefaultValue(false);

        // Index for quick lookup when exchanging code
        builder.HasIndex(c => c.CodeHash);

        // Index for cleanup of expired codes
        builder.HasIndex(c => c.ExpiresAt);

        // Index for OAuth app lookups
        builder.HasIndex(c => c.OAuthAppId);

        // OAuth App relationship
        builder.HasOne(c => c.OAuthApp)
            .WithMany()
            .HasForeignKey(c => c.OAuthAppId)
            .OnDelete(DeleteBehavior.Cascade);

        // User relationship
        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Workspace relationship
        builder.HasOne(c => c.Workspace)
            .WithMany()
            .HasForeignKey(c => c.WorkspaceId)
            .OnDelete(DeleteBehavior.NoAction); // Avoid multiple cascade paths
    }
}

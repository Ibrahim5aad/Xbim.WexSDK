using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class OAuthAppConfiguration : IEntityTypeConfiguration<OAuthApp>
{
    public void Configure(EntityTypeBuilder<OAuthApp> builder)
    {
        builder.ToTable("OAuthApps");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.WorkspaceId)
            .IsRequired();

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.Property(a => a.ClientType)
            .IsRequired();

        builder.Property(a => a.ClientId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.ClientSecretHash)
            .HasMaxLength(512);

        builder.Property(a => a.RedirectUris)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(a => a.AllowedScopes)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(a => a.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.CreatedByUserId)
            .IsRequired();

        // Unique ClientId globally (not just per workspace)
        builder.HasIndex(a => a.ClientId)
            .IsUnique();

        // Index for workspace lookups
        builder.HasIndex(a => a.WorkspaceId);

        // Workspace relationship
        builder.HasOne(a => a.Workspace)
            .WithMany()
            .HasForeignKey(a => a.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Creator relationship
        builder.HasOne(a => a.CreatedByUser)
            .WithMany()
            .HasForeignKey(a => a.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Audit logs relationship
        builder.HasMany(a => a.AuditLogs)
            .WithOne(l => l.OAuthApp)
            .HasForeignKey(l => l.OAuthAppId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable("UploadSessions");

        builder.HasKey(us => us.Id);

        builder.Property(us => us.ProjectId)
            .IsRequired();

        builder.Property(us => us.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(us => us.ContentType)
            .HasMaxLength(200);

        builder.Property(us => us.ExpectedSizeBytes);

        builder.Property(us => us.Status)
            .IsRequired();

        builder.Property(us => us.UploadMode)
            .IsRequired();

        builder.Property(us => us.TempStorageKey)
            .HasMaxLength(1000);

        builder.Property(us => us.DirectUploadUrl)
            .HasMaxLength(2000);

        builder.Property(us => us.CreatedAt)
            .IsRequired();

        builder.Property(us => us.ExpiresAt)
            .IsRequired();

        // Relationship to Project
        builder.HasOne(us => us.Project)
            .WithMany(p => p.UploadSessions)
            .HasForeignKey(us => us.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Relationship to committed File (optional)
        // Use NoAction to avoid multiple cascade paths in SQL Server
        // (Project -> Files cascade conflicts with Project -> UploadSessions -> Files path)
        builder.HasOne(us => us.CommittedFile)
            .WithMany()
            .HasForeignKey(us => us.CommittedFileId)
            .OnDelete(DeleteBehavior.NoAction);

        // Indexes
        builder.HasIndex(us => us.ProjectId);
        builder.HasIndex(us => us.Status);
        builder.HasIndex(us => us.ExpiresAt);
        builder.HasIndex(us => new { us.ProjectId, us.Status });
    }
}

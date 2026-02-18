using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class FileEntityConfiguration : IEntityTypeConfiguration<FileEntity>
{
    public void Configure(EntityTypeBuilder<FileEntity> builder)
    {
        builder.ToTable("Files");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.ProjectId)
            .IsRequired();

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.ContentType)
            .HasMaxLength(200);

        builder.Property(f => f.SizeBytes)
            .IsRequired();

        builder.Property(f => f.Checksum)
            .HasMaxLength(128);

        builder.Property(f => f.Kind)
            .IsRequired();

        builder.Property(f => f.Category)
            .IsRequired();

        builder.Property(f => f.StorageProvider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(f => f.StorageKey)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(f => f.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        // Relationship to Project
        builder.HasOne(f => f.Project)
            .WithMany(p => p.Files)
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(f => f.ProjectId);
        builder.HasIndex(f => f.StorageKey);
        builder.HasIndex(f => new { f.ProjectId, f.IsDeleted });
        builder.HasIndex(f => new { f.ProjectId, f.Kind });
        builder.HasIndex(f => new { f.ProjectId, f.Category });
    }
}

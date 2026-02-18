using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class FileLinkConfiguration : IEntityTypeConfiguration<FileLink>
{
    public void Configure(EntityTypeBuilder<FileLink> builder)
    {
        builder.ToTable("FileLinks");

        builder.HasKey(fl => fl.Id);

        builder.Property(fl => fl.SourceFileId)
            .IsRequired();

        builder.Property(fl => fl.TargetFileId)
            .IsRequired();

        builder.Property(fl => fl.LinkType)
            .IsRequired();

        builder.Property(fl => fl.CreatedAt)
            .IsRequired();

        // Relationship: Source file (the derived/artifact file)
        builder.HasOne(fl => fl.SourceFile)
            .WithMany(f => f.SourceLinks)
            .HasForeignKey(fl => fl.SourceFileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationship: Target file (the original/source file)
        builder.HasOne(fl => fl.TargetFile)
            .WithMany(f => f.TargetLinks)
            .HasForeignKey(fl => fl.TargetFileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(fl => fl.SourceFileId);
        builder.HasIndex(fl => fl.TargetFileId);
        builder.HasIndex(fl => new { fl.SourceFileId, fl.LinkType });
        builder.HasIndex(fl => new { fl.TargetFileId, fl.LinkType });
    }
}

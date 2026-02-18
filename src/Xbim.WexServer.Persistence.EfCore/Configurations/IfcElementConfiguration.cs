using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class IfcElementConfiguration : IEntityTypeConfiguration<IfcElement>
{
    public void Configure(EntityTypeBuilder<IfcElement> builder)
    {
        builder.ToTable("IfcElements");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ModelVersionId)
            .IsRequired();

        builder.Property(e => e.EntityLabel)
            .IsRequired();

        builder.Property(e => e.GlobalId)
            .HasMaxLength(64);

        builder.Property(e => e.Name)
            .HasMaxLength(500);

        builder.Property(e => e.TypeName)
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(2000);

        builder.Property(e => e.ObjectType)
            .HasMaxLength(500);

        builder.Property(e => e.TypeObjectName)
            .HasMaxLength(500);

        builder.Property(e => e.TypeObjectType)
            .HasMaxLength(200);

        builder.Property(e => e.ExtractedAt)
            .IsRequired();

        // Relationship to ModelVersion
        builder.HasOne(e => e.ModelVersion)
            .WithMany()
            .HasForeignKey(e => e.ModelVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes for efficient querying
        builder.HasIndex(e => e.ModelVersionId);
        builder.HasIndex(e => new { e.ModelVersionId, e.EntityLabel }).IsUnique();
        builder.HasIndex(e => new { e.ModelVersionId, e.GlobalId });
        builder.HasIndex(e => new { e.ModelVersionId, e.TypeName });
    }
}

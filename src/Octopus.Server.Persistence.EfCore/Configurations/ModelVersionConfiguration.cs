using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Octopus.Server.Domain.Entities;

namespace Octopus.Server.Persistence.EfCore.Configurations;

public class ModelVersionConfiguration : IEntityTypeConfiguration<ModelVersion>
{
    public void Configure(EntityTypeBuilder<ModelVersion> builder)
    {
        builder.ToTable("ModelVersions");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.ModelId)
            .IsRequired();

        builder.Property(v => v.VersionNumber)
            .IsRequired();

        builder.Property(v => v.IfcFileId)
            .IsRequired();

        builder.Property(v => v.Status)
            .IsRequired();

        builder.Property(v => v.ErrorMessage)
            .HasMaxLength(4000);

        builder.Property(v => v.CreatedAt)
            .IsRequired();

        // Relationship to Model (configured in ModelConfiguration)

        // Relationship to IFC File (required)
        builder.HasOne(v => v.IfcFile)
            .WithMany()
            .HasForeignKey(v => v.IfcFileId)
            .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete - preserve file if version deleted

        // Relationship to WexBIM File (optional artifact)
        // Use NoAction to avoid multiple cascade paths in SQL Server
        // (Project -> Files cascade conflicts with Project -> Models -> Versions -> Files path)
        builder.HasOne(v => v.WexBimFile)
            .WithMany()
            .HasForeignKey(v => v.WexBimFileId)
            .OnDelete(DeleteBehavior.NoAction);

        // Relationship to Properties File (optional artifact)
        // Use NoAction to avoid multiple cascade paths in SQL Server
        builder.HasOne(v => v.PropertiesFile)
            .WithMany()
            .HasForeignKey(v => v.PropertiesFileId)
            .OnDelete(DeleteBehavior.NoAction);

        // Indexes
        builder.HasIndex(v => v.ModelId);
        builder.HasIndex(v => new { v.ModelId, v.VersionNumber }).IsUnique();
        builder.HasIndex(v => v.IfcFileId);
        builder.HasIndex(v => v.Status);
    }
}

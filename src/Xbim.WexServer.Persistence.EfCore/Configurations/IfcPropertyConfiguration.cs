using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class IfcPropertyConfiguration : IEntityTypeConfiguration<IfcProperty>
{
    public void Configure(EntityTypeBuilder<IfcProperty> builder)
    {
        builder.ToTable("IfcProperties");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PropertySetId)
            .IsRequired();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(p => p.Value)
            .HasMaxLength(4000);

        builder.Property(p => p.ValueType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Unit)
            .HasMaxLength(100);

        // Relationship to PropertySet
        builder.HasOne(p => p.PropertySet)
            .WithMany(ps => ps.Properties)
            .HasForeignKey(p => p.PropertySetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(p => p.PropertySetId);
        builder.HasIndex(p => new { p.PropertySetId, p.Name });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Octopus.Server.Domain.Entities;

namespace Octopus.Server.Persistence.EfCore.Configurations;

public class IfcQuantitySetConfiguration : IEntityTypeConfiguration<IfcQuantitySet>
{
    public void Configure(EntityTypeBuilder<IfcQuantitySet> builder)
    {
        builder.ToTable("IfcQuantitySets");

        builder.HasKey(qs => qs.Id);

        builder.Property(qs => qs.ElementId)
            .IsRequired();

        builder.Property(qs => qs.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(qs => qs.GlobalId)
            .HasMaxLength(64);

        // Relationship to Element
        builder.HasOne(qs => qs.Element)
            .WithMany(e => e.QuantitySets)
            .HasForeignKey(qs => qs.ElementId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(qs => qs.ElementId);
        builder.HasIndex(qs => new { qs.ElementId, qs.Name });
    }
}

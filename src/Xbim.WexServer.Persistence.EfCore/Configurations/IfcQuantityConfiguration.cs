using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class IfcQuantityConfiguration : IEntityTypeConfiguration<IfcQuantity>
{
    public void Configure(EntityTypeBuilder<IfcQuantity> builder)
    {
        builder.ToTable("IfcQuantities");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.QuantitySetId)
            .IsRequired();

        builder.Property(q => q.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(q => q.Value);

        builder.Property(q => q.ValueType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(q => q.Unit)
            .HasMaxLength(100);

        // Relationship to QuantitySet
        builder.HasOne(q => q.QuantitySet)
            .WithMany(qs => qs.Quantities)
            .HasForeignKey(q => q.QuantitySetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(q => q.QuantitySetId);
        builder.HasIndex(q => new { q.QuantitySetId, q.Name });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xbim.WexServer.Domain.Entities;

namespace Xbim.WexServer.Persistence.EfCore.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Subject)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.Email)
            .HasMaxLength(256);

        builder.Property(u => u.DisplayName)
            .HasMaxLength(256);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        // Unique constraint on Subject (external identity)
        builder.HasIndex(u => u.Subject)
            .IsUnique();

        // Index on Email for lookups
        builder.HasIndex(u => u.Email);
    }
}

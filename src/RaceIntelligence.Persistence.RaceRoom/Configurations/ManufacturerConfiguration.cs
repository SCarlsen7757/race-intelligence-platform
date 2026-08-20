using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Core.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        builder.ToTable("manufacturers");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(m => m.Name).HasColumnName("name").IsRequired();

        builder.HasIndex(m => m.Name).IsUnique();

        builder.HasMany(m => m.Cars)
            .WithOne(c => c.Manufacturer)
            .HasForeignKey(c => c.ManufacturerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

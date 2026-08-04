using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Configurations;

/// <summary>Maps <see cref="CarClass"/> to the <c>car_classes</c> table.</summary>
public sealed class CarClassConfiguration : IEntityTypeConfiguration<CarClass>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CarClass> builder)
    {
        builder.ToTable("car_classes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.Name).HasColumnName("name").IsRequired();

        builder.HasIndex(c => c.Name).IsUnique();

        builder.HasMany(c => c.Cars)
            .WithOne(c => c.CarClass)
            .HasForeignKey(c => c.CarClassId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

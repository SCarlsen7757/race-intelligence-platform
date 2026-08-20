using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class CarConfiguration : IEntityTypeConfiguration<Car>
{
    public void Configure(EntityTypeBuilder<Car> builder)
    {
        builder.ToTable("cars");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.Name).HasColumnName("name").IsRequired();
        builder.Property(c => c.ManufacturerId).HasColumnName("manufacturer_id");
        builder.Property(c => c.CarClassId).HasColumnName("car_class_id");
        builder.Property(c => c.SimCarId).HasColumnName("sim_car_id").IsRequired();

        builder.HasIndex(c => c.SimCarId).IsUnique();

        builder.HasMany(c => c.Sessions)
            .WithOne(s => s.Car)
            .HasForeignKey(s => s.CarId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

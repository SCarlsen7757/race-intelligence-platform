using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.RaceRoom.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class OperatingWindowConfiguration : IEntityTypeConfiguration<OperatingWindowRow>
{
    public void Configure(EntityTypeBuilder<OperatingWindowRow> builder)
    {
        builder.ToTable("operating_windows");

        // Compound is part of the key and is nullable, which PostgreSQL treats as distinct from
        // every other null in a primary key — so it cannot be one. A unique index with NULLS NOT
        // DISTINCT is what actually means "one row per corner and compound, and 'no compound
        // reported' is itself a compound".
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(w => w.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(w => w.Corner).HasColumnName("corner").HasColumnType("smallint").IsRequired();
        builder.Property(w => w.Compound).HasColumnName("compound");
        builder.Property(w => w.TyreOptimalCelsius).HasColumnName("tyre_optimal_celsius");
        builder.Property(w => w.TyreColdCelsius).HasColumnName("tyre_cold_celsius");
        builder.Property(w => w.TyreHotCelsius).HasColumnName("tyre_hot_celsius");
        builder.Property(w => w.BrakeOptimalCelsius).HasColumnName("brake_optimal_celsius");
        builder.Property(w => w.BrakeColdCelsius).HasColumnName("brake_cold_celsius");
        builder.Property(w => w.BrakeHotCelsius).HasColumnName("brake_hot_celsius");

        builder.HasIndex(w => new { w.SessionId, w.Corner, w.Compound })
            .HasDatabaseName("ux_operating_windows_session_corner_compound")
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasOne<Core.Entities.Session>()
            .WithMany()
            .HasForeignKey(w => w.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

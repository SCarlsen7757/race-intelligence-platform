using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class LapConfiguration : IEntityTypeConfiguration<Lap>
{
    public void Configure(EntityTypeBuilder<Lap> builder)
    {
        builder.ToTable("laps");

        // No surrogate key: a lap is uniquely and permanently identified by its position within
        // its session.
        builder.HasKey(l => new { l.SessionId, l.LapNumber });

        builder.Property(l => l.SessionId).HasColumnName("session_id");
        builder.Property(l => l.LapNumber).HasColumnName("lap_number");
        builder.Property(l => l.LapTime).HasColumnName("lap_time").HasColumnType("interval");
        builder.Property(l => l.FuelUsed).HasColumnName("fuel_used");
        builder.Property(l => l.AvgSpeed).HasColumnName("avg_speed");
        builder.Property(l => l.MaxSpeed).HasColumnName("max_speed");
        builder.Property(l => l.QualityScore).HasColumnName("quality_score");
        builder.Property(l => l.IsValid).HasColumnName("is_valid").IsRequired();
    }
}

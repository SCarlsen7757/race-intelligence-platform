using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Converters;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Configurations;

public sealed class TelemetrySampleConfiguration : IEntityTypeConfiguration<TelemetrySample>
{
    public void Configure(EntityTypeBuilder<TelemetrySample> builder)
    {
        builder.ToTable("telemetry_samples");

        // See the composite-PK rationale documented on the TelemetrySample entity itself
        // (TimescaleDB partitioning-column compatibility + COPY-batch idempotency).
        builder.HasKey(t => new { t.SessionId, t.Timestamp, t.SequenceNumber });

        builder.Property(t => t.SessionId).HasColumnName("session_id");
        builder.Property(t => t.Timestamp).HasColumnName("timestamp");
        builder.Property(t => t.SequenceNumber).HasColumnName("sequence_number");

        builder.Property(t => t.SimulationTime).HasColumnName("simulation_time").IsRequired();
        builder.Property(t => t.LapNumber).HasColumnName("lap_number").IsRequired();
        builder.Property(t => t.Sector).HasColumnName("sector").IsRequired();
        builder.Property(t => t.Speed).HasColumnName("speed").IsRequired();
        builder.Property(t => t.Throttle).HasColumnName("throttle");
        builder.Property(t => t.Brake).HasColumnName("brake");
        builder.Property(t => t.Steering).HasColumnName("steering").IsRequired();
        builder.Property(t => t.Gear).HasColumnName("gear").HasColumnType("smallint");
        builder.Property(t => t.EngineRpm).HasColumnName("engine_rpm").IsRequired();
        builder.Property(t => t.FuelLeft).HasColumnName("fuel_left").IsRequired();
        builder.Property(t => t.Position).HasColumnName("position").HasColumnType("smallint");
        builder.Property(t => t.TrackPositionFraction).HasColumnName("track_position_fraction");

        builder.Property(t => t.WheelSpeed).HasColumnName("wheel_speed").HasColumnType("real[]").IsRequired();
        builder.Property(t => t.SuspensionTravel).HasColumnName("suspension_travel").HasColumnType("real[]").IsRequired();
        builder.Property(t => t.TyrePressure).HasColumnName("tyre_pressure").HasColumnType("real[]");
        builder.Property(t => t.TyreWear).HasColumnName("tyre_wear").HasColumnType("real[]");

        builder.Property(t => t.TyreTemperature)
            .HasColumnName("tyre_temperature")
            .HasConversion(JsonElementConverter.Converter, JsonElementConverter.Comparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.Extras)
            .HasColumnName("extras")
            .HasConversion(JsonElementConverter.Converter, JsonElementConverter.Comparer)
            .HasColumnType("jsonb")
            .IsRequired();

        // BRIN over the future TimescaleDB partitioning column: cheap, effective on
        // naturally time-ordered inserts, and a fraction of a B-tree's size on this table.
        builder.HasIndex(t => t.Timestamp)
            .HasDatabaseName("ix_telemetry_timestamp_brin")
            .HasMethod("brin");

        builder.HasIndex(t => new { t.SessionId, t.LapNumber })
            .HasDatabaseName("ix_telemetry_session_lap");
    }
}

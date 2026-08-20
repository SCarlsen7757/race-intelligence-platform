using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Core.Converters;
using RaceIntelligence.Persistence.Core.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

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
        builder.Property(t => t.Clutch).HasColumnName("clutch");
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
            .HasColumnType("jsonb")
            .IsRequired();

        // RaceRoom-owned promoted extras. These are shadow properties on the canonical entity on
        // purpose: Persistence.Core owns no simulator schema, while this store can query the values
        // RaceRoom exposes without repeatedly parsing jsonb. Negative simulator sentinels are
        // translated to null by RaceRoomExtrasProjector before the bulk write path reaches Postgres.
        builder.Property<int?>("PushToPassAvailable").HasColumnName("push_to_pass_available");
        builder.Property<int?>("PushToPassEngaged").HasColumnName("push_to_pass_engaged");
        builder.Property<int?>("PushToPassAmountLeft").HasColumnName("push_to_pass_amount_left");
        builder.Property<float?>("PushToPassEngagedTimeLeftSeconds").HasColumnName("push_to_pass_engaged_time_left_seconds");
        builder.Property<float?>("PushToPassWaitTimeLeftSeconds").HasColumnName("push_to_pass_wait_time_left_seconds");
        builder.Property<int?>("TyreSubtypeFront").HasColumnName("tyre_subtype_front");
        builder.Property<int?>("TyreSubtypeRear").HasColumnName("tyre_subtype_rear");
        builder.Property<int?>("CutTrackWarnings").HasColumnName("cut_track_warnings");
        builder.Property<float?>("DamageEngine").HasColumnName("damage_engine");
        builder.Property<float?>("DamageTransmission").HasColumnName("damage_transmission");
        builder.Property<float?>("DamageAerodynamics").HasColumnName("damage_aerodynamics");
        builder.Property<float?>("DamageSuspension").HasColumnName("damage_suspension");

        // BRIN over the future TimescaleDB partitioning column: cheap, effective on
        // naturally time-ordered inserts, and a fraction of a B-tree's size on this table.
        builder.HasIndex(t => t.Timestamp)
            .HasDatabaseName("ix_telemetry_timestamp_brin")
            .HasMethod("brin");

        builder.HasIndex(t => new { t.SessionId, t.LapNumber })
            .HasDatabaseName("ix_telemetry_session_lap");
    }
}

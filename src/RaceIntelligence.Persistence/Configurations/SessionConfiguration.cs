using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.Converters;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Configurations;

/// <summary>Maps <see cref="Session"/> to the <c>sessions</c> table.</summary>
public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(s => s.GameVersionId).HasColumnName("game_version_id").IsRequired();
        builder.Property(s => s.DriverId).HasColumnName("driver_id");
        builder.Property(s => s.PlayerName).HasColumnName("player_name");
        builder.Property(s => s.TrackLayoutId).HasColumnName("track_layout_id");
        builder.Property(s => s.CarId).HasColumnName("car_id");
        builder.Property(s => s.SimCarId).HasColumnName("sim_car_id");
        builder.Property(s => s.SimCarClassId).HasColumnName("sim_car_class_id");
        builder.Property(s => s.SimManufacturerId).HasColumnName("sim_manufacturer_id");

        // Stored as smallint, not a native Postgres enum: see TelemetrySample entity remarks.
        builder.Property(s => s.SessionType)
            .HasColumnName("session_type")
            .HasConversion<short>()
            .HasColumnType("smallint")
            .IsRequired();

        // The sim's own raw rate codes, not normalized multipliers — see the entity's remarks.
        // The int -> short narrowing is spelled out rather than left to EF's implicit provider-type
        // resolution (which does infer it from the column type): SessionType above and Capabilities
        // below both name their conversion, and a silent narrowing cast is exactly the thing worth
        // stating out loud.
        builder.Property(s => s.FuelUsageRate)
            .HasColumnName("fuel_usage_rate")
            .HasConversion<short?>()
            .HasColumnType("smallint");

        builder.Property(s => s.TyreWearRate)
            .HasColumnName("tyre_wear_rate")
            .HasConversion<short?>()
            .HasColumnType("smallint");

        builder.Property(s => s.Capabilities)
            .HasColumnName("capabilities")
            .HasConversion(SimCapabilitiesConverter.Converter)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(s => s.SchemaVersion).HasColumnName("schema_version").IsRequired();

        builder.Property(s => s.Weather)
            .HasColumnName("weather")
            .HasConversion(JsonElementConverter.NullableConverter, JsonElementConverter.NullableComparer)
            .HasColumnType("jsonb");

        builder.Property(s => s.Setup)
            .HasColumnName("setup")
            .HasConversion(JsonElementConverter.NullableConverter, JsonElementConverter.NullableComparer)
            .HasColumnType("jsonb");

        builder.Property(s => s.Extras)
            .HasColumnName("extras")
            .HasConversion(JsonElementConverter.Converter, JsonElementConverter.Comparer)
            .HasColumnType("jsonb");

        builder.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(s => s.EndedAt).HasColumnName("ended_at");

        builder.HasIndex(s => s.GameVersionId).HasDatabaseName("ix_sessions_game_version");

        // The shape of every "compare driver A against driver B under the same rules" query:
        // a driver's sessions narrowed to one wear/fuel rate combination.
        builder.HasIndex(s => new { s.DriverId, s.TyreWearRate, s.FuelUsageRate })
            .HasDatabaseName("ix_sessions_driver_wear_rates");

        builder.HasMany(s => s.Laps)
            .WithOne(l => l.Session)
            .HasForeignKey(l => l.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.TelemetrySamples)
            .WithOne(t => t.Session)
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

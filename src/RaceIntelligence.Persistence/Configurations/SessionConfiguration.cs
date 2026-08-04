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

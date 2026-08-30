using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceIntelligence.Persistence.RaceRoom.Entities;

namespace RaceIntelligence.Persistence.RaceRoom.Configurations;

public sealed class TelemetrySampleConfiguration : IEntityTypeConfiguration<TelemetrySample>
{
    public void Configure(EntityTypeBuilder<TelemetrySample> builder)
    {
        builder.ToTable("telemetry_samples");

        // See the composite-PK rationale documented on the TelemetrySample entity itself
        // (TimescaleDB partitioning-column compatibility + COPY-batch idempotency).
        builder.HasKey(t => new { t.SessionId, t.Timestamp, t.SequenceNumber });

        // Every column name, store type and nullability comes from the channel manifest, in the
        // same order the bulk writer writes them. Nothing is named twice.
        TelemetrySample.ConfigureChannels(builder);

        builder.HasOne<Core.Entities.Session>()
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // BRIN over the future TimescaleDB partitioning column: cheap, effective on
        // naturally time-ordered inserts, and a fraction of a B-tree's size on this table.
        builder.HasIndex(t => t.Timestamp)
            .HasDatabaseName("ix_telemetry_timestamp_brin")
            .HasMethod("brin");

        builder.HasIndex(t => new { t.SessionId, t.LapNumber })
            .HasDatabaseName("ix_telemetry_session_lap");
    }
}

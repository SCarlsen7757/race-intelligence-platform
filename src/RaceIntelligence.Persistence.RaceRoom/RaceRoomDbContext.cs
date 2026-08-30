using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core;
using RaceIntelligence.Persistence.RaceRoom.Entities;

namespace RaceIntelligence.Persistence.RaceRoom;

/// <summary>
/// RaceRoom's telemetry store: the canonical model, mapped to RaceRoom's own schema.
/// </summary>
/// <remarks>
/// <para>
/// One database per simulator (ADR 0001), so this owns the tables and the migrations while
/// <see cref="TelemetryDbContext"/> owns only the shapes that are genuinely shared. Since #109 the
/// telemetry sample is one of the things that is not: every RaceRoom channel is a typed column here,
/// declared by this assembly, and nothing about push-to-pass or third-spring velocity leaks into a
/// model a second simulator would inherit.
/// </para>
/// <para>
/// All table and column naming is explicit snake_case configured per entity in
/// <c>Configurations/*.cs</c> rather than through a naming-convention package. A second simulator
/// is a sibling of this project and may name the same concepts differently; nothing here is shared
/// with it beyond the entity types themselves.
/// </para>
/// </remarks>
/// <param name="options">The configured options, typically built via <c>UseNpgsql</c>.</param>
public sealed class RaceRoomDbContext(DbContextOptions<RaceRoomDbContext> options)
    : TelemetryDbContext(options)
{
    /// <summary>Raw, immutable telemetry samples. Insert-only; see the remarks on the entity.</summary>
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();

    /// <summary>The tyre and brake temperature bands each corner ran in, per compound.</summary>
    public DbSet<OperatingWindowRow> OperatingWindows => Set<OperatingWindowRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // This assembly's configurations, not Core's — Core has none, and that is the invariant
        // that keeps it schema-free.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RaceRoomDbContext).Assembly);
    }
}

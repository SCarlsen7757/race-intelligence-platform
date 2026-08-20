using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core;

namespace RaceIntelligence.Persistence.RaceRoom;

/// <summary>
/// RaceRoom's telemetry store: the canonical model, mapped to RaceRoom's own schema.
/// </summary>
/// <remarks>
/// <para>
/// One database per simulator (ADR 0001), so this owns the tables and the migrations while
/// <see cref="TelemetryDbContext"/> owns only the shape. Everything RaceRoom treats as first-class
/// and the canonical model does not name — push-to-pass, tyre subtype, cut-track warnings — belongs
/// here as typed columns rather than in a JSON blob, which is the whole point of the split.
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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // This assembly's configurations, not Core's — Core has none, and that is the invariant
        // that keeps it schema-free.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RaceRoomDbContext).Assembly);
    }
}

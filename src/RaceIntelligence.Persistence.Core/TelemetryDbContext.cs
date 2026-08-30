using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core.Entities;

namespace RaceIntelligence.Persistence.Core;

/// <summary>
/// The canonical telemetry model's shape, with no schema attached.
/// </summary>
/// <remarks>
/// <para>
/// <b>This context declares no tables.</b> It names the entity sets every simulator's store has —
/// which is what lets the repositories, mappers and converters be written once — and says nothing
/// about what any of them are called in a database. There is no <c>OnModelCreating</c> here on
/// purpose: mapping is a simulator's own business, because storage is one database per simulator
/// and each is free to shape its schema to what that simulator actually exposes (ADR 0001).
/// </para>
/// <para>
/// A simulator derives from this, applies its own configurations, and owns its own migrations. See
/// <c>RaceIntelligence.Persistence.RaceRoom</c> for the first one; a second simulator adds a
/// project beside it and changes nothing here.
/// </para>
/// <para>
/// <b>Raw telemetry is insert-only.</b> <see cref="TelemetrySamples"/> is exposed as a
/// <see cref="DbSet{TEntity}"/> for querying and migrations, but the actual write path is a binary
/// <c>COPY</c> that bypasses <c>SaveChanges</c> entirely — see each simulator's bulk writer. Nothing
/// in this project updates or deletes a <see cref="TelemetrySample"/>.
/// </para>
/// </remarks>
/// <param name="options">
/// The configured options. Untyped, because a derived context is constructed with
/// <c>DbContextOptions&lt;ThatContext&gt;</c> and the base cannot name it.
/// </param>
public abstract class TelemetryDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<GameVersion> GameVersions => Set<GameVersion>();

    public DbSet<Driver> Drivers => Set<Driver>();

    public DbSet<Track> Tracks => Set<Track>();

    public DbSet<TrackLayout> TrackLayouts => Set<TrackLayout>();

    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();

    public DbSet<CarClass> CarClasses => Set<CarClass>();

    public DbSet<Car> Cars => Set<Car>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Lap> Laps => Set<Lap>();

    // There is deliberately no telemetry DbSet here. Since #109 the sample is RaceRoom's — a
    // hundred and seventy-five columns naming push-to-pass, DRS and third-spring velocity are not a
    // shape a second simulator would inherit — so it is declared, configured and exposed by
    // RaceRoomDbContext. What stays above is what genuinely is shared: sessions, laps, and the
    // reference data they point at.
}

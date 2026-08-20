using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence;

/// <summary>
/// EF Core context for the Race Intelligence platform's PostgreSQL store.
/// </summary>
/// <remarks>
/// <para>
/// All table/column naming is explicit snake_case configured per-entity in
/// <c>Configurations/*.cs</c> (applied via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>)
/// rather than via a naming-convention package — see each configuration for its exact mapping.
/// </para>
/// <para>
/// <b>Raw telemetry is insert-only.</b> <see cref="TelemetrySamples"/> is exposed as a
/// <see cref="DbSet{TEntity}"/> for querying/migrations, but the platform's actual write path for
/// telemetry is <c>Bulk/NpgsqlTelemetryWriter</c>, which bypasses <c>SaveChanges</c> entirely via a
/// binary <c>COPY</c>. Nothing in this project updates or deletes a <see cref="TelemetrySample"/>.
/// </para>
/// </remarks>
/// <param name="options">The configured <see cref="DbContextOptions{TContext}"/>, typically built via <c>UseNpgsql</c>.</param>
public sealed class RaceIntelligenceDbContext(DbContextOptions<RaceIntelligenceDbContext> options)
    : DbContext(options)
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

    /// <summary>Raw, immutable telemetry samples. See remarks on this type for the insert-only write path.</summary>
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RaceIntelligenceDbContext).Assembly);
    }
}

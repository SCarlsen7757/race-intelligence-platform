using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Identity.Entities;

namespace RaceIntelligence.Identity;

/// <summary>
/// EF Core context for the cross-simulator identity registry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own database, deliberately separate from every simulator's.</b> Storage is one database
/// per simulator, and this is the one thing that cannot be: it must outlive any of them. Restoring
/// RaceRoom's database from a backup must not take iRacing's mapping with it, and a simulator
/// nobody plays any more must not be what a person's identity depends on.
/// </para>
/// <para>
/// It is also the only hand-curated state in the platform. Everything else here is either raw
/// telemetry the collector produced or something derived from it and rebuildable; these two tables
/// are the record of a human saying "these are the same person", and nothing can regenerate them.
/// Back them up accordingly.
/// </para>
/// <para>
/// Naming follows the same convention as the telemetry store: explicit snake_case configured
/// per-entity in <c>Configurations/*.cs</c>, not a convention package.
/// </para>
/// </remarks>
/// <param name="options">The configured <see cref="DbContextOptions{TContext}"/>, typically built via <c>UseNpgsql</c>.</param>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();

    public DbSet<PersonSimAlias> PersonSimAliases => Set<PersonSimAlias>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}

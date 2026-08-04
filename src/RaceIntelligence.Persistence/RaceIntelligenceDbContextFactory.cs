using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RaceIntelligence.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> construct a <see cref="RaceIntelligenceDbContext"/> at design time (e.g.
/// <c>dotnet ef migrations add</c>) without a full host/DI setup, since this project is a plain
/// class library with no <c>Program.cs</c> of its own.
/// </summary>
/// <remarks>
/// The connection string is read from the <c>RACEINTEL_CONNECTIONSTRING</c> environment variable
/// so design-time tooling never has to guess or hardcode real infrastructure; a localhost fallback
/// is provided purely so the common local-dev case (a Postgres instance on the default port) works
/// without any setup. This factory is never used at runtime — the host application configures
/// <see cref="RaceIntelligenceDbContext"/> via normal DI/<c>UseNpgsql</c>.
/// </remarks>
public sealed class RaceIntelligenceDbContextFactory : IDesignTimeDbContextFactory<RaceIntelligenceDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=race_intelligence;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public RaceIntelligenceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RACEINTEL_CONNECTIONSTRING") ?? FallbackConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<RaceIntelligenceDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new RaceIntelligenceDbContext(optionsBuilder.Options);
    }
}

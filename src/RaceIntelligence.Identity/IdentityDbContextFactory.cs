using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RaceIntelligence.Identity;

/// <summary>
/// Lets <c>dotnet ef</c> construct an <see cref="IdentityDbContext"/> at design time without a full
/// host/DI setup, since this project is a plain class library with no <c>Program.cs</c> of its own.
/// </summary>
/// <remarks>
/// Its own environment variable rather than the telemetry store's, because this is a different
/// database and pointing migrations at the wrong one is exactly the mistake worth making impossible.
/// Never used at runtime — the host configures <see cref="IdentityDbContext"/> through normal DI.
/// </remarks>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=race_intelligence_identity;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RACEINTEL_IDENTITY_CONNECTIONSTRING") ?? FallbackConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new IdentityDbContext(optionsBuilder.Options);
    }
}

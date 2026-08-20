using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace RaceIntelligence.Identity.Tests.Support;

/// <summary>
/// Starts a single PostgreSQL Testcontainer for this assembly and applies the registry's migrations
/// to it once.
/// </summary>
/// <remarks>
/// Its own container rather than the telemetry suite's, mirroring the deployment: the registry lives
/// in a database of its own precisely so it does not share a lifetime with any simulator's store,
/// and a test fixture that put both schemas in one database would be testing an arrangement the
/// platform does not have.
/// <para>
/// If Docker is unavailable, <see cref="InitializeAsync"/> records the reason in
/// <see cref="SkipReason"/> rather than throwing, so the suite reports honest skips instead of
/// failures. Guard with
/// <c>Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Postgres unavailable");</c>.
/// </para>
/// </remarks>
public sealed class IdentityPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary><see langword="true"/> once the container started and migrations applied successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason <see cref="IsAvailable"/> is <see langword="false"/>, if it is.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>Connection string for the running container. Only valid when <see cref="IsAvailable"/>.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync().ConfigureAwait(false);

            ConnectionString = _container.GetConnectionString();

            await using var db = CreateContext();
            await db.Database.MigrateAsync().ConfigureAwait(false);

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = $"Docker/Testcontainers unavailable in this environment: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Creates a new <see cref="IdentityDbContext"/> against the running container.</summary>
    public IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new IdentityDbContext(options);
    }
}

/// <summary>xunit collection wiring a single shared <see cref="IdentityPostgresFixture"/> across this assembly.</summary>
[CollectionDefinition(Name)]
public sealed class IdentityPostgresCollection : ICollectionFixture<IdentityPostgresFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(IdentityPostgresCollection.Name)]</c>.</summary>
    public const string Name = "IdentityPostgres";
}

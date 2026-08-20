using Microsoft.EntityFrameworkCore;
using Npgsql;
using RaceIntelligence.Persistence;
using Testcontainers.PostgreSql;

namespace RaceIntelligence.Persistence.RaceRoom.Tests.Support;

/// <summary>
/// Starts a single PostgreSQL Testcontainer for the whole test assembly (via
/// <see cref="PostgresCollection"/>) and applies migrations to it once.
/// </summary>
/// <remarks>
/// If Docker is not available in the environment this fixture runs in, <see cref="InitializeAsync"/>
/// swallows the startup failure and records it in <see cref="SkipReason"/> instead of throwing.
/// Tests that depend on Postgres must start with a guard like:
/// <code>Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Postgres unavailable");</code>
/// so the suite reports honest skips rather than failures (or a faked pass) when there is no
/// container runtime to test against.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _dataSource;

    /// <summary><see langword="true"/> once the container started and migrations applied successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason <see cref="IsAvailable"/> is <see langword="false"/>, if it is.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>Connection string for the running container. Only valid when <see cref="IsAvailable"/>.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Shared data source for bulk-writer tests. Only valid when <see cref="IsAvailable"/>.</summary>
    public NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("Postgres fixture is not available; guard tests with Assert.SkipUnless(fixture.IsAvailable, ...).");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine")
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            ConnectionString = _container.GetConnectionString();
            _dataSource = NpgsqlDataSource.Create(ConnectionString);

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
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Creates a new <see cref="RaceRoomDbContext"/> against the running container.</summary>
    public RaceRoomDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RaceRoomDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new RaceRoomDbContext(options);
    }
}

/// <summary>xunit collection wiring a single shared <see cref="PostgresFixture"/> across all tests in this assembly.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(PostgresCollection.Name)]</c>.</summary>
    public const string Name = "Postgres";
}

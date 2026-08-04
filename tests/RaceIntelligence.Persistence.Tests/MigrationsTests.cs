using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.Tests;

/// <summary>Verifies the <c>InitialCreate</c> migration applies cleanly and produces the schema the platform relies on.</summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migrations_apply_cleanly_to_a_real_postgres_container()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();

        // The collection fixture already migrated once; MigrateAsync here must be a clean no-op,
        // proving the migration is idempotent/re-runnable as well as initially applicable.
        var pendingBefore = await db.Database.GetPendingMigrationsAsync();
        pendingBefore.ShouldBeEmpty();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.ShouldContain("InitialCreate");
    }

    [Fact]
    public async Task Telemetry_timestamp_brin_index_exists()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_telemetry_timestamp_brin'";
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue("expected the BRIN index to exist");
        var indexDef = reader.GetString(0);
        indexDef.ToLowerInvariant().ShouldContain("using brin");
    }

    [Fact]
    public async Task Telemetry_samples_and_laps_have_composite_primary_keys()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        (await PrimaryKeyColumnsAsync(connection, "laps")).ShouldBe(["session_id", "lap_number"]);
        (await PrimaryKeyColumnsAsync(connection, "telemetry_samples")).ShouldBe(["session_id", "timestamp", "sequence_number"]);
    }

    private static async Task<List<string>> PrimaryKeyColumnsAsync(System.Data.Common.DbConnection connection, string table)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT a.attname
            FROM pg_index i
            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = @table::regclass AND i.indisprimary
            ORDER BY array_position(i.indkey, a.attnum)
            """;
        var param = cmd.CreateParameter();
        param.ParameterName = "table";
        param.Value = table;
        cmd.Parameters.Add(param);

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}

using RaceIntelligence.Persistence.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

/// <summary>
/// Regression coverage for the explicit <c>ValueComparer&lt;JsonElement&gt;</c> documented on
/// <see cref="RaceIntelligence.Persistence.Core.Converters.JsonElementConverter"/>: without it, every
/// jsonb-backed property looks permanently modified because each materialization produces a new
/// <see cref="System.Text.Json.JsonElement"/> instance — which would mean a spurious UPDATE for every
/// session ever loaded.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ValueComparerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Loading_an_entity_with_jsonb_columns_and_touching_nothing_reports_no_changes()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db, SampleFactory.NonTrivialExtras());

        // Only the session carries jsonb now: a telemetry sample's extras and tyre_temperature are
        // typed columns since #109, so there is nothing on that table for a comparer to get wrong.
        _ = await Ef.SingleAsync(db.Sessions, s => s.Id == sessionId);

        db.ChangeTracker.HasChanges().ShouldBeFalse(
            "loading an entity and touching nothing must not be seen as a change; " +
            "if this fails, the JsonElement ValueComparer regressed.");

        // DetectChanges should also produce zero modified entries, not just HasChanges() == false.
        db.ChangeTracker.DetectChanges();
        db.ChangeTracker.Entries().ShouldAllBe(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Unchanged);
    }
}

using RaceIntelligence.Persistence.Mapping;
using RaceIntelligence.Persistence.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.Tests;

/// <summary>
/// Regression coverage for the explicit <c>ValueComparer&lt;JsonElement&gt;</c> documented on
/// <see cref="RaceIntelligence.Persistence.Converters.JsonElementConverter"/>: without it, every
/// jsonb-backed property looks permanently modified because each materialization produces a new
/// <see cref="System.Text.Json.JsonElement"/> instance.
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

        var sample = SampleFactory.TelemetrySample(sessionId, sequenceNumber: 1, extras: SampleFactory.NonTrivialExtrasText);
        db.TelemetrySamples.Add(TelemetrySampleMapper.ToEntity(sample));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Load both a jsonb-bearing Session (weather/setup/extras) and TelemetrySample
        // (tyre_temperature/extras) without mutating anything.
        _ = await Ef.SingleAsync(db.Sessions, s => s.Id == sessionId);
        _ = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == sessionId);

        db.ChangeTracker.HasChanges().ShouldBeFalse(
            "loading an entity and touching nothing must not be seen as a change; " +
            "if this fails, the JsonElement ValueComparer regressed.");

        // DetectChanges should also produce zero modified entries, not just HasChanges() == false.
        db.ChangeTracker.DetectChanges();
        db.ChangeTracker.Entries().ShouldAllBe(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Unchanged);
    }
}

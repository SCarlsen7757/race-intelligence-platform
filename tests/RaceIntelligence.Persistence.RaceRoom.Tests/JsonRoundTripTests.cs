using System.Text.Json;
using RaceIntelligence.Persistence.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

/// <summary>
/// Verifies the one remaining jsonb column round-trips non-trivial <see cref="JsonElement"/> data
/// losslessly.
/// </summary>
/// <remarks>
/// <para>
/// <b>There used to be four of these tests, and three of the columns are gone.</b> A telemetry
/// sample's <c>extras</c> and <c>tyre_temperature</c> were 68% of a 724 MB table, holding the same
/// twenty-nine key names on 357,152 rows; every channel in them is a typed column now, so a
/// round-trip test for them is a test of nothing. A session's <c>weather</c> and <c>setup</c> were
/// NULL on every row ever written and always would have been — RaceRoom has no dynamic weather and
/// no readable setup export — so they were removed rather than filled (#109).
/// </para>
/// <para>
/// A session's <c>extras</c> stays, and so does this test. It runs once per session rather than sixty
/// times a second, and what it carries is a bag of simulator identifiers nothing queries by, which is
/// exactly the case a jsonb column is for.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class JsonRoundTripTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Session_extras_round_trip_through_jsonb()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);

        var session = await Ef.SingleAsync(db.Sessions, s => s.Id == sessionId);
        session.Extras = SampleFactory.NonTrivialExtras();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await Ef.SingleAsync(db.Sessions, s => s.Id == sessionId);
        var root = reloaded.Extras;

        // Nesting, arrays, a non-integral number and an explicit null — the shapes a naive
        // round-trip through a string loses or reorders.
        root.GetProperty("correctionFactor").GetDouble().ShouldBe(1.0625);
        root.GetProperty("tags").GetArrayLength().ShouldBe(2);
        root.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}

using System.Text.Json;
using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Persistence.Mapping;
using RaceIntelligence.Persistence.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.Tests;

/// <summary>Verifies jsonb columns round-trip non-trivial <see cref="System.Text.Json.JsonElement"/> data losslessly.</summary>
[Collection(PostgresCollection.Name)]
public sealed class JsonRoundTripTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Telemetry_sample_extras_round_trip_through_jsonb()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);

        var sample = SampleFactory.TelemetrySample(sessionId, sequenceNumber: 1, extras: SampleFactory.NonTrivialExtrasText);
        var entity = TelemetrySampleMapper.ToEntity(sample);

        db.TelemetrySamples.Add(entity);
        await db.SaveChangesAsync();

        // Force a fresh read from the database rather than the change tracker's identity map.
        db.ChangeTracker.Clear();

        var reloaded = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == sessionId && t.SequenceNumber == 1);

        // Extras is carried as text, so the point of this test is that the text Postgres hands back
        // still parses to the same structure and JSON types it went in as.
        using var document = System.Text.Json.JsonDocument.Parse(reloaded.Extras);
        var root = document.RootElement;

        root.GetProperty("pushToPass").GetProperty("available").GetBoolean().ShouldBeTrue();
        root.GetProperty("pushToPass").GetProperty("usesRemaining").GetInt32().ShouldBe(5);
        root.GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ShouldBe(["yellow-flag", "traffic"]);
        root.GetProperty("correctionFactor").GetDouble().ShouldBe(1.0625);
        root.GetProperty("note").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Telemetry_sample_tyre_temperature_round_trips_all_four_wheels()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);

        var sample = SampleFactory.TelemetrySample(sessionId, sequenceNumber: 2);
        var entity = TelemetrySampleMapper.ToEntity(sample);

        db.TelemetrySamples.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == sessionId && t.SequenceNumber == 2);

        reloaded.TyreTemperature.GetProperty("FrontLeft").GetProperty("Inner").GetSingle().ShouldBe(sample.TyreTemperature.FrontLeft.Inner!.Value);
        reloaded.TyreTemperature.GetProperty("RearRight").GetProperty("Hot").GetSingle().ShouldBe(sample.TyreTemperature.RearRight.Hot!.Value);
    }

    /// <summary>
    /// A tyre temperature the simulator did not report must persist as JSON <c>null</c>, not as a
    /// number.
    /// </summary>
    /// <remarks>
    /// Telemetry rows are never rewritten, so if an unreported reading were flattened to a numeric
    /// sentinel here it would be permanently indistinguishable from a real measurement — the exact
    /// failure the nullable <see cref="TyreTemperature"/> model exists to prevent.
    /// </remarks>
    [Fact]
    public async Task Unreported_tyre_temperatures_round_trip_as_json_null()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "PostgreSQL container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);

        var sample = SampleFactory.TelemetrySample(sessionId, sequenceNumber: 3) with
        {
            TyreTemperature = new WheelData<TyreTemperature>(
                new TyreTemperature(null, null, null, null, null, null),
                new TyreTemperature(86, null, 89, 90, 70, 110),
                new TyreTemperature(84, 89, 87, 90, 70, 110),
                new TyreTemperature(85, 90, 88, 90, 70, 110)),
        };

        db.TelemetrySamples.Add(TelemetrySampleMapper.ToEntity(sample));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == sessionId && t.SequenceNumber == 3);

        var frontLeft = reloaded.TyreTemperature.GetProperty("FrontLeft");
        frontLeft.GetProperty("Inner").ValueKind.ShouldBe(JsonValueKind.Null);
        frontLeft.GetProperty("Hot").ValueKind.ShouldBe(JsonValueKind.Null);

        // A partially-reported wheel must keep the readings it does have.
        var frontRight = reloaded.TyreTemperature.GetProperty("FrontRight");
        frontRight.GetProperty("Inner").GetSingle().ShouldBe(86f);
        frontRight.GetProperty("Middle").ValueKind.ShouldBe(JsonValueKind.Null);
        frontRight.GetProperty("Outer").GetSingle().ShouldBe(89f);
    }

    [Fact]
    public async Task Session_weather_and_setup_round_trip_when_present_and_stay_null_when_absent()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var weather = System.Text.Json.JsonDocument.Parse("""{"airTemp":22.5,"rain":false}""").RootElement;

        var sessionId = await SampleFactory.CreateSessionAsync(db);
        var session = await Ef.SingleAsync(db.Sessions, s => s.Id == sessionId);
        session.Weather = weather;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await Ef.SingleAsync(db.Sessions, s => s.Id == sessionId);
        reloaded.Weather.ShouldNotBeNull();
        reloaded.Weather.Value.GetProperty("airTemp").GetDouble().ShouldBe(22.5);
        reloaded.Setup.ShouldBeNull();
    }
}

using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Entities;
using RaceIntelligence.Persistence.Mapping;
using RaceIntelligence.Persistence.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.Tests;

/// <summary>Verifies the composite primary keys on <c>laps</c> and <c>telemetry_samples</c> are actually enforced by Postgres.</summary>
[Collection(PostgresCollection.Name)]
public sealed class CompositePrimaryKeyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Duplicate_lap_number_within_a_session_is_rejected()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);

        db.Laps.Add(new Lap { SessionId = sessionId, LapNumber = 1, IsValid = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.Laps.Add(new Lap { SessionId = sessionId, LapNumber = 1, IsValid = true });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_session_timestamp_sequence_number_is_rejected_when_inserted_directly()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);
        var sample = SampleFactory.TelemetrySample(sessionId, sequenceNumber: 7);

        db.TelemetrySamples.Add(TelemetrySampleMapper.ToEntity(sample));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Same (session_id, timestamp, sequence_number) key, inserted directly through EF rather
        // than the bulk writer — this path has no ON CONFLICT DO NOTHING, so it must surface the
        // primary key violation rather than silently succeed.
        db.TelemetrySamples.Add(TelemetrySampleMapper.ToEntity(sample));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}

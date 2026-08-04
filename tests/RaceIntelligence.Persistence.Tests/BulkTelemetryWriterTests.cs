using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Bulk;
using RaceIntelligence.Persistence.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.Tests;

/// <summary>Verifies <see cref="NpgsqlTelemetryWriter"/>'s binary-COPY write path and its idempotency guarantee.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BulkTelemetryWriterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task First_write_inserts_every_sample_with_no_duplicates()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);
        var samples = SampleFactory.TelemetryBatch(sessionId, count: 50);

        var writer = new NpgsqlTelemetryWriter(fixture.DataSource);
        var result = await writer.WriteAsync(sessionId, samples);

        result.Inserted.ShouldBe(50);
        result.Duplicates.ShouldBe(0);

        var rowCount = await db.TelemetrySamples.CountAsync(t => t.SessionId == sessionId);
        rowCount.ShouldBe(50);
    }

    [Fact]
    public async Task Re_writing_the_same_batch_is_idempotent()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);
        var samples = SampleFactory.TelemetryBatch(sessionId, count: 30);
        var writer = new NpgsqlTelemetryWriter(fixture.DataSource);

        var first = await writer.WriteAsync(sessionId, samples);
        first.Inserted.ShouldBe(30);
        first.Duplicates.ShouldBe(0);

        // Simulate a retried upload: the exact same batch, byte for byte, submitted again.
        var second = await writer.WriteAsync(sessionId, samples);
        second.Inserted.ShouldBe(0);
        second.Duplicates.ShouldBe(30);

        var rowCount = await db.TelemetrySamples.CountAsync(t => t.SessionId == sessionId);
        rowCount.ShouldBe(30, "the retried batch must not have created duplicate rows");
    }

    [Fact]
    public async Task Overlapping_batch_only_inserts_the_new_rows()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);
        var writer = new NpgsqlTelemetryWriter(fixture.DataSource);

        var anchor = DateTimeOffset.UtcNow;
        var firstBatch = SampleFactory.TelemetryBatch(sessionId, count: 20, startSequence: 0, anchor: anchor);
        await writer.WriteAsync(sessionId, firstBatch);

        // Shares the same anchor, so sequence numbers 10..19 land on identical (session_id,
        // timestamp, sequence_number) keys as the first batch and must be detected as duplicates;
        // 20..29 are genuinely new.
        var secondBatch = SampleFactory.TelemetryBatch(sessionId, count: 20, startSequence: 10, anchor: anchor);
        var result = await writer.WriteAsync(sessionId, secondBatch);

        result.Inserted.ShouldBe(10);
        result.Duplicates.ShouldBe(10);

        var rowCount = await db.TelemetrySamples.CountAsync(t => t.SessionId == sessionId);
        rowCount.ShouldBe(30);
    }

    [Fact]
    public async Task Empty_batch_is_a_no_op()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        var sessionId = Guid.CreateVersion7();
        var writer = new NpgsqlTelemetryWriter(fixture.DataSource);

        var result = await writer.WriteAsync(sessionId, []);

        result.Inserted.ShouldBe(0);
        result.Duplicates.ShouldBe(0);
    }
}

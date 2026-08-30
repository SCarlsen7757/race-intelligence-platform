using Microsoft.EntityFrameworkCore;
using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Persistence.Core.Bulk;
using RaceIntelligence.Persistence.RaceRoom.Bulk;
using RaceIntelligence.Persistence.RaceRoom.Entities;
using RaceIntelligence.Persistence.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

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

    /// <summary>
    /// Compares every column written by the binary <c>COPY</c> path against the same sample written
    /// through EF Core.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test the whole design exists for.</b> A binary <c>COPY</c> takes a column list
    /// and a stream of positional values and checks neither against the other: if the two disagree,
    /// Postgres writes camber into ride height and reports success. Both are generated from one loop
    /// over one manifest, so the mismatch is no longer expressible — and this compares all hundred
    /// and seventy-five columns against an independent writer to prove it.
    /// </para>
    /// <para>
    /// Reflective on purpose. A hand-written assertion per column would be the thing it is checking:
    /// a second list, kept by hand, that can silently disagree with the first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Copy_path_writes_every_column_where_the_ef_path_writes_it()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var copySessionId = await SampleFactory.CreateSessionAsync(db);
        var efSessionId = await SampleFactory.CreateSessionAsync(db);

        var timestamp = DateTimeOffset.UtcNow;
        var sample = SampleFactory.TelemetrySample(copySessionId, sequenceNumber: 7, timestamp: timestamp) with
        {
            // A scatter of unreported channels, so "null column" is compared as carefully as a value
            // is. These are the ones a wrong-position write would most plausibly fill.
            Position = null,
            Throttle = null,
            TrackPositionFraction = null,
            Gear = null,
            TyrePressureFr = null,
            TyreTempFlInner = null,
            CamberRr = null,
            DownforceNewtons = null,
        };

        await new NpgsqlTelemetryWriter(fixture.DataSource).WriteAsync(copySessionId, [sample]);

        db.TelemetrySamples.Add(TelemetrySample.FromDto(sample with { SessionId = efSessionId }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var viaCopy = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == copySessionId);
        var viaEf = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == efSessionId);

        // SessionId is the one column that legitimately differs: it is what tells the two rows apart.
        var channels = typeof(TelemetrySample)
            .GetProperties()
            .Where(property => property.Name != nameof(TelemetrySample.SessionId))
            .ToList();

        channels.Count.ShouldBeGreaterThan(150, "the manifest should have generated a property per channel");

        foreach (var channel in channels)
        {
            channel.GetValue(viaCopy).ShouldBe(
                channel.GetValue(viaEf),
                $"{channel.Name} differs between the COPY path and the EF path");
        }

        viaCopy.Gear.ShouldBeNull("an unreported gear is a null column, never the -2 sentinel or 0");
        viaCopy.TyreWearRr.ShouldBeNull("an unreported corner is a null column, not a brand-new tyre");
    }

    /// <summary>
    /// The column list the writer names and the order the table actually has, compared against each
    /// other in the database.
    /// </summary>
    /// <remarks>
    /// The <c>COPY</c> names its columns explicitly, so a table whose ordinals differ is not by
    /// itself a bug — but the temp table is created <c>LIKE telemetry_samples</c> and the fold-in is
    /// <c>INSERT ... SELECT {list}</c>, so the two staying in step is what keeps that pair readable.
    /// It also catches a manifest column the migration never grew.
    /// </remarks>
    [Fact]
    public async Task The_generated_column_list_matches_the_table()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_name = 'telemetry_samples'
            ORDER BY ordinal_position
            """;

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.ShouldBe(TelemetrySample.Columns);
    }

    /// <summary>
    /// One row must not carry another's values. The per-wheel scratch arrays this used to guard are
    /// gone — every corner is its own column now — but a batch is still one <c>COPY</c> stream, and a
    /// row boundary written in the wrong place would shift every value after it.
    /// </summary>
    [Fact]
    public async Task Rows_in_one_batch_keep_their_own_per_wheel_values()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);
        var anchor = DateTimeOffset.UtcNow;

        var samples = Enumerable.Range(0, 5)
            .Select(i => SampleFactory.TelemetrySample(sessionId, i, anchor.AddMilliseconds(i * 20)) with
            {
                WheelSpeedFl = i,
                WheelSpeedFr = i + 0.1f,
                WheelSpeedRl = i + 0.2f,
                WheelSpeedRr = i + 0.3f,
                TyrePressureFl = i % 2 == 0 ? i : null,
                TyrePressureFr = null,
                TyrePressureRl = i % 2 == 0 ? i + 2 : null,
                TyrePressureRr = i % 2 == 0 ? i + 3 : null,
            })
            .ToList();

        await new NpgsqlTelemetryWriter(fixture.DataSource).WriteAsync(sessionId, samples);

        var stored = await db.TelemetrySamples
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.SequenceNumber)
            .ToListAsync();

        stored.Count.ShouldBe(5);
        for (var i = 0; i < 5; i++)
        {
            new float?[] { stored[i].WheelSpeedFl, stored[i].WheelSpeedFr, stored[i].WheelSpeedRl, stored[i].WheelSpeedRr }
                .ShouldBe([i, i + 0.1f, i + 0.2f, i + 0.3f]);

            if (i % 2 == 0)
            {
                stored[i].TyrePressureFl.ShouldBe(i);
                stored[i].TyrePressureRl.ShouldBe(i + 2);
            }
            else
            {
                stored[i].TyrePressureFl.ShouldBeNull();
                stored[i].TyrePressureRl.ShouldBeNull();
            }

            stored[i].TyrePressureFr.ShouldBeNull();
        }
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

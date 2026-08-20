using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Persistence.Core.Bulk;
using RaceIntelligence.Persistence.RaceRoom.Bulk;
using RaceIntelligence.Persistence.Core.Mapping;
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
    /// The binary <c>COPY</c> path builds its row without going through
    /// <see cref="RaceIntelligence.Persistence.Core.Mapping.TelemetrySampleMapper.ToEntity"/>, so nothing
    /// but a column-by-column comparison against the EF path proves the two still agree.
    /// </summary>
    [Fact]
    public async Task Copy_path_writes_the_same_column_values_as_the_ef_path()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var copySessionId = await SampleFactory.CreateSessionAsync(db);
        var efSessionId = await SampleFactory.CreateSessionAsync(db);

        var timestamp = DateTimeOffset.UtcNow;
        var sample = SampleFactory.TelemetrySample(
            copySessionId,
            sequenceNumber: 7,
            timestamp: timestamp,
            extras: """{"tags":["yellow-flag","traffic"],"correctionFactor":1.0625,"note":null}""")
            with
            {
                // One wheel unreported on each nullable array, and an all-null array, so the
                // "null column vs. array of nulls" distinction is compared too.
                TyrePressure = new WheelData<float?>(180f, null, 175f, 175f),
                TyreWear = new WheelData<float?>(null, null, null, null),
                TyreTemperature = new WheelData<TyreTemperature>(
                    new TyreTemperature(null, 90, 88, 90, 70, 110),
                    new TyreTemperature(86, 91, 89, 90, 70, 110),
                    new TyreTemperature(84, 89, 87, 90, 70, 110),
                    new TyreTemperature(85, 90, 88, 90, null, 110)),
                Position = null,
                Throttle = null,
                TrackPositionFraction = null,
                Gear = null,
            };

        await new NpgsqlTelemetryWriter(fixture.DataSource).WriteAsync(copySessionId, [sample]);

        db.TelemetrySamples.Add(TelemetrySampleMapper.ToEntity(sample with { SessionId = efSessionId }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var viaCopy = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == copySessionId);
        var viaEf = await Ef.SingleAsync(db.TelemetrySamples, t => t.SessionId == efSessionId);

        viaCopy.Timestamp.ShouldBe(viaEf.Timestamp);
        viaCopy.SequenceNumber.ShouldBe(viaEf.SequenceNumber);
        viaCopy.SimulationTime.ShouldBe(viaEf.SimulationTime);
        viaCopy.LapNumber.ShouldBe(viaEf.LapNumber);
        viaCopy.Sector.ShouldBe(viaEf.Sector);
        viaCopy.Speed.ShouldBe(viaEf.Speed);
        viaCopy.Throttle.ShouldBe(viaEf.Throttle);
        viaCopy.Brake.ShouldBe(viaEf.Brake);
        viaCopy.Steering.ShouldBe(viaEf.Steering);
        viaCopy.Gear.ShouldBe(viaEf.Gear);
        viaCopy.Gear.ShouldBeNull("an unreported gear is a null column, never the -2 sentinel or 0");
        viaCopy.EngineRpm.ShouldBe(viaEf.EngineRpm);
        viaCopy.FuelLeft.ShouldBe(viaEf.FuelLeft);
        viaCopy.Position.ShouldBe(viaEf.Position);
        viaCopy.TrackPositionFraction.ShouldBe(viaEf.TrackPositionFraction);
        viaCopy.WheelSpeed.ShouldBe(viaEf.WheelSpeed);
        viaCopy.SuspensionTravel.ShouldBe(viaEf.SuspensionTravel);
        viaCopy.TyrePressure.ShouldBe(viaEf.TyrePressure);
        viaCopy.TyreWear.ShouldBe(viaEf.TyreWear);
        viaCopy.TyreWear.ShouldBeNull("an all-unreported wheel array is a null column, not an array of nulls");
        viaCopy.TyreTemperature.GetRawText().ShouldBe(viaEf.TyreTemperature.GetRawText());
        viaCopy.Extras.ShouldBe(viaEf.Extras);
    }

    [Fact]
    public async Task Copy_path_projects_all_raceroom_columns_and_keeps_only_unpromoted_extras()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var sessionId = await SampleFactory.CreateSessionAsync(db);
        const string extras =
            """
            {
              "pushToPass": {
                "available": 1, "engaged": 0, "amountLeft": -1,
                "engagedTimeLeftSeconds": 2.5, "waitTimeLeftSeconds": -1.0,
                "boostMode": "attack"
              },
              "tireSubtypeFront": 3, "tireSubtypeRear": -1,
              "cutTrackWarnings": 0,
              "damage": {
                "engine": 0.0, "transmission": 0.25, "aerodynamics": -1.0,
                "suspension": 1.0, "chassis": 0.75
              },
              "unknown": [1, 2, 3]
            }
            """;

        var sample = SampleFactory.TelemetrySample(sessionId, 901, extras: extras);
        await new NpgsqlTelemetryWriter(fixture.DataSource).WriteAsync(sessionId, [sample]);

        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT push_to_pass_available, push_to_pass_engaged, push_to_pass_amount_left,
                   push_to_pass_engaged_time_left_seconds, push_to_pass_wait_time_left_seconds,
                   tyre_subtype_front, tyre_subtype_rear, cut_track_warnings,
                   damage_engine, damage_transmission, damage_aerodynamics, damage_suspension,
                   extras::text
            FROM telemetry_samples
            WHERE session_id = @session_id AND sequence_number = 901
            """;
        command.Parameters.AddWithValue("session_id", sessionId);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(1);
        reader.GetInt32(1).ShouldBe(0, "a real zero must survive sentinel conversion");
        (await reader.IsDBNullAsync(2)).ShouldBeTrue();
        reader.GetFloat(3).ShouldBe(2.5f);
        (await reader.IsDBNullAsync(4)).ShouldBeTrue();
        reader.GetInt32(5).ShouldBe(3);
        (await reader.IsDBNullAsync(6)).ShouldBeTrue();
        reader.GetInt32(7).ShouldBe(0, "zero cut-track warnings is a reported value");
        reader.GetFloat(8).ShouldBe(0f);
        reader.GetFloat(9).ShouldBe(0.25f);
        (await reader.IsDBNullAsync(10)).ShouldBeTrue();
        reader.GetFloat(11).ShouldBe(1f);

        using var storedExtras = System.Text.Json.JsonDocument.Parse(reader.GetString(12));
        var root = storedExtras.RootElement;
        root.GetProperty("unknown").GetArrayLength().ShouldBe(3);
        root.GetProperty("pushToPass").GetProperty("boostMode").GetString().ShouldBe("attack");
        root.GetProperty("damage").GetProperty("chassis").GetSingle().ShouldBe(0.75f);
        root.TryGetProperty("tireSubtypeFront", out _).ShouldBeFalse();
        root.TryGetProperty("cutTrackWarnings", out _).ShouldBeFalse();
    }

    /// <summary>
    /// The per-wheel scratch arrays are reused across rows in one batch, so a bug there would show
    /// up as every row carrying the last row's values.
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
                WheelSpeed = new WheelData<float>(i, i + 0.1f, i + 0.2f, i + 0.3f),
                TyrePressure = i % 2 == 0
                    ? new WheelData<float?>(i, null, i + 2, i + 3)
                    : new WheelData<float?>(null, null, null, null),
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
            stored[i].WheelSpeed.ShouldBe([i, i + 0.1f, i + 0.2f, i + 0.3f]);
            if (i % 2 == 0)
            {
                stored[i].TyrePressure.ShouldBe([i, null, i + 2, i + 3]);
            }
            else
            {
                stored[i].TyrePressure.ShouldBeNull();
            }
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

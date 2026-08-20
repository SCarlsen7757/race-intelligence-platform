using Npgsql;
using RaceIntelligence.Persistence.Bulk;
using NpgsqlTypes;
using RaceIntelligence.Persistence.Converters;
using RaceIntelligence.Persistence.Mapping;
using CoreTelemetry = RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Persistence.RaceRoom.Bulk;

/// <summary>
/// Bulk-inserts raw telemetry via Postgres binary <c>COPY</c>, bypassing EF Core's change tracker
/// and <c>SaveChanges</c> entirely.
/// </summary>
/// <remarks>
/// <para>
/// Telemetry is the hot path: a 30-minute session can produce tens of thousands of samples, and
/// going through EF's change tracking, SQL generation, and per-row round trips for that volume
/// would be needlessly slow. Instead this writer:
/// </para>
/// <list type="number">
/// <item>Opens a plain <see cref="NpgsqlConnection"/> and starts one transaction for the whole batch.</item>
/// <item>
/// Creates a connection-scoped <c>TEMP TABLE ... (LIKE telemetry_samples INCLUDING DEFAULTS) ON
/// COMMIT DROP</c> — same columns as <c>telemetry_samples</c>, but with none of its constraints
/// (in particular, no primary key), so the binary import itself can never fail or slow down on a
/// duplicate.
/// </item>
/// <item>Streams every row into that temp table with <see cref="NpgsqlBinaryImporter"/>, the fastest write path Npgsql exposes.</item>
/// <item>
/// Folds the temp table into <c>telemetry_samples</c> with a single
/// <c>INSERT ... SELECT ... ON CONFLICT DO NOTHING</c>, which is where duplicate detection
/// actually happens, against the real primary key <c>(session_id, timestamp, sequence_number)</c>.
/// </item>
/// <item>Commits. The temp table disappears (<c>ON COMMIT DROP</c>) with the transaction.</item>
/// </list>
/// <para>
/// This makes retried upload batches safe by construction: the collector can re-send a batch after
/// a network blip without coordinating with the server about what already arrived, because
/// <c>sequence_number</c> (collector-assigned and monotonic per session) makes the primary key —
/// and therefore the conflict target — exactly reproducible across retries.
/// </para>
/// <para>
/// <b>Column type notes for the binary COPY protocol</b> (verified against Npgsql 10):
/// <c>timestamptz</c> accepts a <see cref="DateTimeOffset"/> directly (Npgsql normalizes it to the
/// stored UTC instant regardless of the offset carried); <c>real[]</c>/<c>real?[]</c> arrays are
/// written via <see cref="NpgsqlDbType.Array"/> combined with <see cref="NpgsqlDbType.Real"/>;
/// <c>jsonb</c> is written as a UTF-8 string tagged <see cref="NpgsqlDbType.Jsonb"/>, not as a
/// <c>json</c>/text-typed value, or Postgres rejects the binary payload; <c>smallint</c> columns
/// (<c>gear</c>, <c>position</c>) are written as <see cref="short"/>.
/// </para>
/// </remarks>
/// <param name="dataSource">
/// The <see cref="NpgsqlDataSource"/> to open bulk-import connections from. Kept separate from
/// <see cref="TelemetryDbContext"/> deliberately — this writer never touches the change
/// tracker, so it has no reason to share a context instance or its lifetime.
/// </param>
public sealed class NpgsqlTelemetryWriter(NpgsqlDataSource dataSource) : ITelemetryWriter
{
    private const string TempTableName = "tmp_telemetry_import";

    private static readonly string[] Columns =
    [
        "session_id", "timestamp", "sequence_number", "simulation_time", "lap_number", "sector",
        "speed", "throttle", "brake", "clutch", "steering", "gear", "engine_rpm", "fuel_left", "position",
        "track_position_fraction", "wheel_speed", "suspension_travel", "tyre_pressure", "tyre_wear",
        "tyre_temperature", "extras",
    ];

    private static readonly string ColumnList = string.Join(", ", Columns);

    /// <summary>
    /// Writes a batch of telemetry samples for <paramref name="sessionId"/>, idempotently.
    /// Re-submitting a batch (or a batch overlapping one already written, e.g. after a retried
    /// upload) never creates duplicate rows: samples whose primary key
    /// <c>(session_id, timestamp, sequence_number)</c> already exists are silently skipped and
    /// counted as <see cref="TelemetryWriteResult.Duplicates"/>.
    /// </summary>
    /// <param name="sessionId">
    /// The session every sample in <paramref name="samples"/> is expected to belong to. Used to
    /// validate the batch; every sample's own <c>SessionId</c> must match.
    /// </param>
    /// <param name="samples">The samples to write. May be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <inheritdoc />
    public async Task<TelemetryWriteResult> WriteAsync(
        Guid sessionId,
        IReadOnlyList<CoreTelemetry.TelemetrySample> samples,
        CancellationToken ct = default)
    {
        if (samples.Count == 0)
        {
            return new TelemetryWriteResult(0, 0);
        }

        foreach (var sample in samples)
        {
            if (sample.SessionId != sessionId)
            {
                throw new ArgumentException(
                    $"Sample with sequence number {sample.SequenceNumber} belongs to session {sample.SessionId}, not {sessionId}.",
                    nameof(samples));
            }
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var createTempTable = new NpgsqlCommand(
            $"CREATE TEMP TABLE {TempTableName} (LIKE telemetry_samples INCLUDING DEFAULTS) ON COMMIT DROP",
            connection,
            transaction))
        {
            await createTempTable.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
            $"COPY {TempTableName} ({ColumnList}) FROM STDIN (FORMAT BINARY)",
            ct).ConfigureAwait(false))
        {
            // The four per-wheel arrays are built once and refilled per row. Npgsql copies their
            // contents into the COPY buffer during the write, so reuse is safe — and it turns four
            // allocations per sample (240 a second, per active session) into four per batch.
            var buffers = new RowBuffers();

            foreach (var sample in samples)
            {
                await WriteRowAsync(importer, sample, buffers, ct).ConfigureAwait(false);
            }

            await importer.CompleteAsync(ct).ConfigureAwait(false);
        }

        int inserted;
        await using (var insertFromTemp = new NpgsqlCommand(
            $"""
             INSERT INTO telemetry_samples ({ColumnList})
             SELECT {ColumnList} FROM {TempTableName}
             ON CONFLICT DO NOTHING
             """,
            connection,
            transaction))
        {
            inserted = await insertFromTemp.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new TelemetryWriteResult(inserted, samples.Count - inserted);
    }

    /// <summary>
    /// Writes one sample straight from its canonical form. Deliberately does <i>not</i> go through
    /// <see cref="TelemetrySampleMapper.ToEntity"/>: that builds a tracked-entity-shaped object this
    /// path immediately discards, and every field it copies is one this method reads anyway.
    /// </summary>
    private static async Task WriteRowAsync(
        NpgsqlBinaryImporter importer,
        CoreTelemetry.TelemetrySample sample,
        RowBuffers buffers,
        CancellationToken ct)
    {
        await importer.StartRowAsync(ct).ConfigureAwait(false);

        await importer.WriteAsync(sample.SessionId, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.Timestamp, NpgsqlDbType.TimestampTz, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.SequenceNumber, NpgsqlDbType.Bigint, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.SimulationTime, NpgsqlDbType.Double, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.LapNumber, NpgsqlDbType.Integer, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.Sector, NpgsqlDbType.Integer, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.Speed, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, sample.Throttle, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, sample.Brake, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, sample.Clutch, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.Steering, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync<short>(
            importer,
            sample.Gear.HasValue ? TelemetrySampleMapper.ToSmallInt(sample.Gear.Value) : null,
            NpgsqlDbType.Smallint,
            ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.EngineRpm, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.FuelLeft, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync<short>(
            importer,
            sample.Position.HasValue ? TelemetrySampleMapper.ToSmallInt(sample.Position.Value) : null,
            NpgsqlDbType.Smallint,
            ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, sample.TrackPositionFraction, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(Fill(buffers.WheelSpeed, sample.WheelSpeed), NpgsqlDbType.Array | NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(Fill(buffers.SuspensionTravel, sample.SuspensionTravel), NpgsqlDbType.Array | NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableArrayAsync(importer, Fill(buffers.TyrePressure, sample.TyrePressure), ct).ConfigureAwait(false);
        await WriteNullableArrayAsync(importer, Fill(buffers.TyreWear, sample.TyreWear), ct).ConfigureAwait(false);
        await importer.WriteAsync(
            TelemetrySampleMapper.SerializeTyreTemperatureText(sample.TyreTemperature), NpgsqlDbType.Jsonb, ct).ConfigureAwait(false);
        await importer.WriteAsync(sample.Extras, NpgsqlDbType.Jsonb, ct).ConfigureAwait(false);
    }

    private static float[] Fill(float[] buffer, CoreTelemetry.WheelData<float> wheelData)
    {
        buffer[0] = wheelData.FrontLeft;
        buffer[1] = wheelData.FrontRight;
        buffer[2] = wheelData.RearLeft;
        buffer[3] = wheelData.RearRight;
        return buffer;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> in FL/FR/RL/RR order, or returns <see langword="null"/> when
    /// no wheel reported anything — matching <see cref="TelemetrySampleMapper.ToNullableArray"/>,
    /// which is what the EF path writes into the same nullable <c>real[]</c> columns.
    /// </summary>
    private static float?[]? Fill(float?[] buffer, CoreTelemetry.WheelData<float?> wheelData)
    {
        if (wheelData is { FrontLeft: null, FrontRight: null, RearLeft: null, RearRight: null })
        {
            return null;
        }

        buffer[0] = wheelData.FrontLeft;
        buffer[1] = wheelData.FrontRight;
        buffer[2] = wheelData.RearLeft;
        buffer[3] = wheelData.RearRight;
        return buffer;
    }

    private sealed class RowBuffers
    {
        public float[] WheelSpeed { get; } = new float[4];

        public float[] SuspensionTravel { get; } = new float[4];

        public float?[] TyrePressure { get; } = new float?[4];

        public float?[] TyreWear { get; } = new float?[4];
    }

    private static async Task WriteNullableAsync<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken ct)
        where T : struct
    {
        if (value.HasValue)
        {
            await importer.WriteAsync(value.Value, type, ct).ConfigureAwait(false);
        }
        else
        {
            await importer.WriteNullAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task WriteNullableArrayAsync(NpgsqlBinaryImporter importer, float?[]? values, CancellationToken ct)
    {
        if (values is null)
        {
            await importer.WriteNullAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await importer.WriteAsync(values, NpgsqlDbType.Array | NpgsqlDbType.Real, ct).ConfigureAwait(false);
        }
    }
}

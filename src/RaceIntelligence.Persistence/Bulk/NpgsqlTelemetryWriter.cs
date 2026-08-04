using Npgsql;
using NpgsqlTypes;
using RaceIntelligence.Persistence.Converters;
using RaceIntelligence.Persistence.Mapping;
using CoreTelemetry = RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Persistence.Bulk;

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
/// <see cref="RaceIntelligenceDbContext"/> deliberately — this writer never touches the change
/// tracker, so it has no reason to share a context instance or its lifetime.
/// </param>
public sealed class NpgsqlTelemetryWriter(NpgsqlDataSource dataSource) : ITelemetryWriter
{
    private const string TempTableName = "tmp_telemetry_import";

    private static readonly string[] Columns =
    [
        "session_id", "timestamp", "sequence_number", "simulation_time", "lap_number", "sector",
        "speed", "throttle", "brake", "steering", "gear", "engine_rpm", "fuel_left", "position",
        "track_position_fraction", "wheel_speed", "suspension_travel", "tyre_pressure", "tyre_wear",
        "tyre_temperature", "extras",
    ];

    private static readonly string ColumnList = string.Join(", ", Columns);

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
            foreach (var sample in samples)
            {
                await WriteRowAsync(importer, sample, ct).ConfigureAwait(false);
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

    private static async Task WriteRowAsync(NpgsqlBinaryImporter importer, CoreTelemetry.TelemetrySample sample, CancellationToken ct)
    {
        var row = TelemetrySampleMapper.ToEntity(sample);

        await importer.StartRowAsync(ct).ConfigureAwait(false);

        await importer.WriteAsync(row.SessionId, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.Timestamp, NpgsqlDbType.TimestampTz, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.SequenceNumber, NpgsqlDbType.Bigint, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.SimulationTime, NpgsqlDbType.Double, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.LapNumber, NpgsqlDbType.Integer, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.Sector, NpgsqlDbType.Integer, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.Speed, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, row.Throttle, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, row.Brake, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.Steering, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.Gear, NpgsqlDbType.Smallint, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.EngineRpm, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.FuelLeft, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, row.Position, NpgsqlDbType.Smallint, ct).ConfigureAwait(false);
        await WriteNullableAsync(importer, row.TrackPositionFraction, NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.WheelSpeed, NpgsqlDbType.Array | NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await importer.WriteAsync(row.SuspensionTravel, NpgsqlDbType.Array | NpgsqlDbType.Real, ct).ConfigureAwait(false);
        await WriteNullableArrayAsync(importer, row.TyrePressure, ct).ConfigureAwait(false);
        await WriteNullableArrayAsync(importer, row.TyreWear, ct).ConfigureAwait(false);
        await importer.WriteAsync(JsonElementConverter.Serialize(row.TyreTemperature), NpgsqlDbType.Jsonb, ct).ConfigureAwait(false);
        await importer.WriteAsync(JsonElementConverter.Serialize(row.Extras), NpgsqlDbType.Jsonb, ct).ConfigureAwait(false);
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

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core;
using RaceIntelligence.Persistence.Core.Entities;
using RaceIntelligence.Persistence.RaceRoom.Entities;
using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Persistence.RaceRoom.Repositories;

/// <summary>
/// Reads stored telemetry samples.
/// </summary>
/// <remarks>
/// <b>Always scoped to one lap.</b> <c>telemetry_samples</c> is one row per sample with no blob and
/// no compression, so a session at 60 Hz is hundreds of thousands of rows and "give me the session"
/// is a request that succeeds slowly and then exhausts something. A lap is the unit every chart in
/// the handover backlog actually plots, and it is the unit the schema is already indexed for:
/// <c>ix_telemetry_session_lap</c> on <c>(session_id, lap_number)</c>.
/// <para>
/// The write path is <c>Bulk/ITelemetryWriter</c> and a binary <c>COPY</c>; this is the read path,
/// and the two never share a connection. Nothing here writes — the table is insert-only, and this
/// repository does not even have the vocabulary to change that.
/// </para>
/// </remarks>
/// <param name="db">The simulator's telemetry store, in its schema-free shape.</param>
public sealed class TelemetryReadRepository(RaceRoomDbContext db)
{
    /// <summary>How many samples one lap recorded.</summary>
    /// <remarks>
    /// Asked before the samples themselves so an oversized lap can be refused with a count in the
    /// message rather than by streaming it and failing partway. A count over the same index the
    /// read uses is cheap.
    /// </remarks>
    public async Task<int> CountForLapAsync(Guid sessionId, int lapNumber, CancellationToken ct = default) =>
        await db.TelemetrySamples
            .AsNoTracking()
            .CountAsync(t => t.SessionId == sessionId && t.LapNumber == lapNumber, ct)
            .ConfigureAwait(false);

    /// <summary>
    /// Every sample of one lap, in capture order.
    /// </summary>
    /// <remarks>
    /// Ordered by <c>sequence_number</c> rather than <c>timestamp</c>. The sequence is
    /// collector-assigned and monotonic within a session, which is exactly the guarantee a chart's
    /// x-axis needs; a wall clock can repeat or step backwards, and the primary key orders on it
    /// only because TimescaleDB will one day want it to.
    /// <para>
    /// Projected into <see cref="LapSample"/> in the database rather than materialising
    /// <see cref="TelemetrySample"/>, so the per-wheel <c>real[]</c> columns and the two
    /// <c>jsonb</c> ones are never fetched. They are the widest part of the row and no caller of
    /// this method reads them.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<LapSample>> ListForLapAsync(
        Guid sessionId,
        int lapNumber,
        CancellationToken ct = default) =>
        await db.TelemetrySamples
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.LapNumber == lapNumber)
            .OrderBy(t => t.SequenceNumber)
            .Select(t => new LapSample(
                t.SequenceNumber,
                t.Timestamp,
                t.SimulationTime,
                t.LapNumber,
                t.Sector,
                t.Speed,
                t.Throttle,
                t.Brake,
                t.Clutch,
                t.Steering,
                t.Gear,
                t.EngineRpm,
                t.FuelLeft,
                t.Position,
                t.TrackPositionFraction))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>
    /// The requested channels for every sample of one lap, in capture order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only the requested columns are read.</b> A sample is a hundred and seventy-five columns and
    /// a lap is several thousand samples, so "select the row and let the caller pick" would move tens
    /// of megabytes out of Postgres to draw one line. The projection happens in the database, which
    /// is the whole point of asking for channels by name.
    /// </para>
    /// <para>
    /// <b>The column names come from the manifest, never from the request.</b> The caller hands over
    /// <see cref="RaceRoomChannels.Channel"/> values it looked up by name; a name that is not a
    /// channel never reaches here, and there is no path from request text into this SQL.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ListChannelsForLapAsync(
        Guid sessionId,
        int lapNumber,
        IReadOnlyList<RaceRoomChannels.Channel> channels,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(channels);

        if (channels.Count == 0)
        {
            return [];
        }

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();

        var columns = string.Join(", ", channels.Select(channel => channel.Column));
        command.CommandText =
            $"SELECT {columns} FROM telemetry_samples " +
            "WHERE session_id = @session_id AND lap_number = @lap_number " +
            "ORDER BY sequence_number";
        command.Parameters.Add(Parameter(command, "session_id", sessionId));
        command.Parameters.Add(Parameter(command, "lap_number", lapNumber));

        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = new Dictionary<string, object?>(channels.Count, StringComparer.Ordinal);
                for (var i = 0; i < channels.Count; i++)
                {
                    // Absent stays absent. A null column means the simulator did not report the
                    // channel, and a response that carried it as a JSON null would be saying the
                    // same thing at greater length — the wire omits it instead.
                    if (!await reader.IsDBNullAsync(i, ct).ConfigureAwait(false))
                    {
                        row[channels[i].Name] = reader.GetValue(i);
                    }
                }

                rows.Add(row);
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static DbParameter Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    /// <summary>Which lap numbers a session actually has samples for, ascending.</summary>
    /// <remarks>
    /// Not the same question as "which laps does this session have", which
    /// <see cref="SessionReadRepository.ListLapsAsync"/> answers from the <c>laps</c> table. A lap
    /// row is written when a lap completes; samples arrive throughout. So an in-progress lap has
    /// samples and no lap row, and a lap recorded before telemetry upload caught up has the
    /// reverse. A picker offering laps to chart wants this one.
    /// </remarks>
    public async Task<IReadOnlyList<int>> ListSampledLapNumbersAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.TelemetrySamples
            .AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .Select(t => t.LapNumber)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}

/// <summary>
/// The canonical scalar channels of one telemetry sample.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="TelemetrySample"/>: it omits the per-wheel arrays and the
/// two jsonb columns so the projection stays inside the database. Widen it when something needs a
/// wheel channel, not before.
/// </remarks>
public sealed record LapSample(
    long SequenceNumber,
    DateTimeOffset Timestamp,
    double SimulationTime,
    int LapNumber,
    int Sector,
    float Speed,
    float? Throttle,
    float? Brake,
    float? Clutch,
    float Steering,
    short? Gear,
    float EngineRpm,
    float FuelLeft,
    short? Position,
    float? TrackPositionFraction);

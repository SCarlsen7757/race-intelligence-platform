using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core.Entities;

namespace RaceIntelligence.Persistence.Core.Repositories;

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
public sealed class TelemetryReadRepository(TelemetryDbContext db)
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

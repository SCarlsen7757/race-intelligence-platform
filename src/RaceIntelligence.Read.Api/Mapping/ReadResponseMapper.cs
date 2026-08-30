using RaceIntelligence.Persistence.Core.Entities;
using RaceIntelligence.Persistence.RaceRoom.Repositories;
using RaceIntelligence.Read.Api.Contracts;

namespace RaceIntelligence.Read.Api.Mapping;

/// <summary>
/// Turns stored rows into the shapes this API returns.
/// </summary>
/// <remarks>
/// The read-side counterpart of <c>RaceIntelligence.Ingest.Contracts/Mapping</c>, and separate from
/// the endpoints for the same reason those are: the projection is the part worth testing without a
/// database in the room.
/// </remarks>
internal static class ReadResponseMapper
{
    /// <summary>One session summary.</summary>
    public static SessionSummaryResponse ToResponse(this SessionListRow row) => new(
        row.Session.Id,
        row.Session.StartedAt,
        row.Session.EndedAt,
        row.DriverName,
        row.Session.PlayerName,
        row.TrackName,
        row.LayoutName,
        row.CarName,
        row.Session.SessionType,
        row.Session.FuelUsageRate,
        row.Session.TyreWearRate,
        row.LapCount,
        row.SampleCount);

    /// <summary>One lap summary.</summary>
    /// <remarks>
    /// <see cref="Lap.LapTime"/> is a <see cref="TimeSpan"/> in an <c>interval</c> column and
    /// becomes milliseconds here, matching the <c>...Ms</c> convention the live contracts already
    /// use. A JSON duration has no agreed representation, and the dashboard already has exactly one
    /// habit for reading them.
    /// </remarks>
    public static LapResponse ToResponse(this Lap lap) => new(
        lap.LapNumber,
        lap.LapTime?.TotalMilliseconds,
        lap.FuelUsed,
        lap.AvgSpeed,
        lap.MaxSpeed,
        lap.IsValid);

    /// <summary>One telemetry sample, optionally carrying the extra channels asked for.</summary>
    public static TelemetrySampleResponse ToResponse(
        this LapSample sample,
        IReadOnlyDictionary<string, object?>? channels = null) => new(
        sample.SequenceNumber,
        sample.Timestamp,
        sample.SimulationTime,
        sample.LapNumber,
        sample.Sector,
        sample.Speed,
        sample.Throttle,
        sample.Brake,
        sample.Clutch,
        sample.Steering,
        sample.Gear,
        sample.EngineRpm,
        sample.FuelLeft,
        sample.Position,
        sample.TrackPositionFraction,
        channels);
}

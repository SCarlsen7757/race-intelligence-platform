using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Read.Api.Contracts;

/// <summary>
/// One stored session, as a picker needs it: enough to recognise and choose, nothing more.
/// </summary>
/// <remarks>
/// The read-side counterpart of <c>docs/queries/session-overview.sql</c>, which is this shape
/// written as SQL and is where the joins below come from.
/// <para>
/// <paramref name="SessionType"/>, <paramref name="FuelUsageRate"/> and
/// <paramref name="TyreWearRate"/> are the <b>simulator's own raw codes, untranslated</b> — the same
/// values the collector stored, because normalising them belongs to the sim-aware translator
/// (ADR 0002) and not to a read endpoint. For RaceRoom the rates encode <c>-1</c> = not available,
/// <c>0</c> = off, <c>1</c>–<c>4</c> = 1x–4x. Note <c>-1</c> sorts below <c>0</c>: "the rate was on"
/// is <c>&gt; 0</c>, never "non-zero".
/// </para>
/// </remarks>
/// <param name="SessionId">The session's id, and the key for every other read.</param>
/// <param name="StartedAtUtc">When the session began.</param>
/// <param name="EndedAtUtc">When it ended, or <see langword="null"/> if it never recorded an end.</param>
/// <param name="DriverName">The driver's current display name, if the session has a driver.</param>
/// <param name="PlayerName">The name reported for this session specifically — what to show when the driver has since renamed themselves.</param>
/// <param name="TrackName">Track name, if the layout resolved.</param>
/// <param name="LayoutName">Layout name, if the layout resolved.</param>
/// <param name="CarName">Car name, if the car resolved.</param>
/// <param name="SessionType">The simulator's raw session-type code. See remarks.</param>
/// <param name="FuelUsageRate">The simulator's raw fuel-rate code. See remarks.</param>
/// <param name="TyreWearRate">The simulator's raw tyre-wear-rate code. See remarks.</param>
/// <param name="LapCount">How many laps were recorded.</param>
/// <param name="SampleCount">How many telemetry samples were recorded. Zero means there is nothing to chart.</param>
public sealed record SessionSummaryResponse(
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? DriverName,
    string? PlayerName,
    string? TrackName,
    string? LayoutName,
    string? CarName,
    SessionType SessionType,
    int? FuelUsageRate,
    int? TyreWearRate,
    int LapCount,
    int SampleCount);

/// <summary>A page of sessions, newest first, with the cursor that continues it.</summary>
/// <param name="Sessions">The page, ordered by <see cref="SessionSummaryResponse.StartedAtUtc"/> descending.</param>
/// <param name="NextBefore">
/// Pass as <c>?before=</c> to fetch the next page, or <see langword="null"/> when this page is the
/// last. A cursor rather than an offset so a session written while someone is paging cannot shift
/// the window and hide a row.
/// </param>
public sealed record SessionPageResponse(
    IReadOnlyList<SessionSummaryResponse> Sessions,
    DateTimeOffset? NextBefore);

/// <summary>One lap's summary statistics.</summary>
/// <remarks>
/// Speeds are metres per second, as stored — the canonical unit throughout the platform. Converting
/// for display is the dashboard's job, and doing it here would put two unit conventions on one wire.
/// </remarks>
/// <param name="LapNumber">The lap's number within its session.</param>
/// <param name="LapTimeMs">Lap time in milliseconds, or <see langword="null"/> if the lap never completed.</param>
/// <param name="FuelUsed">Fuel consumed over the lap, in litres, if known.</param>
/// <param name="AvgSpeed">Average speed over the lap, in m/s, if known.</param>
/// <param name="MaxSpeed">Maximum speed during the lap, in m/s, if known.</param>
/// <param name="IsValid">Whether the simulator considered the lap valid.</param>
public sealed record LapResponse(
    int LapNumber,
    double? LapTimeMs,
    float? FuelUsed,
    float? AvgSpeed,
    float? MaxSpeed,
    bool IsValid);

/// <summary>One telemetry sample, as stored.</summary>
/// <remarks>
/// <b>Canonical channels only.</b> A simulator's promoted columns — RaceRoom's <c>push_to_pass_*</c>,
/// <c>tyre_subtype_*</c>, <c>cut_track_warnings</c> and <c>damage_*</c> — are EF <i>shadow</i>
/// properties declared by that simulator's configuration, so they are not on
/// <see cref="RaceIntelligence.Persistence.Core.Entities.TelemetrySample"/> and cannot be reached
/// from this project at all. That is the point: this contract is the same in every simulator.
/// A host that wants its own promoted channels projects them with <c>EF.Property&lt;T&gt;(...)</c>
/// into its own response type rather than widening this one.
/// <para>
/// Per-wheel arrays are ordered [FL, FR, RL, RR], as everywhere else in the platform.
/// </para>
/// </remarks>
/// <param name="SequenceNumber">Collector-assigned, monotonically increasing within the session. The x-axis a chart should trust.</param>
/// <param name="TimestampUtc">Wall-clock capture time.</param>
/// <param name="SimulationTime">In-session time in seconds since the session began.</param>
/// <param name="LapNumber">Lap this sample belongs to.</param>
/// <param name="Sector">Track sector.</param>
/// <param name="Speed">Vehicle speed, m/s.</param>
/// <param name="Throttle">Throttle, 0–1, or <see langword="null"/> if unreported.</param>
/// <param name="Brake">Brake, 0–1, or <see langword="null"/> if unreported.</param>
/// <param name="Clutch">Clutch, 0–1, or <see langword="null"/> if unreported.</param>
/// <param name="Steering">Steering, -1 (full left) to 1 (full right).</param>
/// <param name="Gear">-1 reverse, 0 neutral, greater than 0 forward gear. <see langword="null"/> if unreported.</param>
/// <param name="EngineRpm">Engine speed, rpm.</param>
/// <param name="FuelLeft">Fuel remaining, litres.</param>
/// <param name="Position">Race position, or <see langword="null"/>.</param>
/// <param name="TrackPositionFraction">Position around the lap, 0–1, or <see langword="null"/>.</param>
/// <param name="Channels">
/// The extra channels this request asked for, by name, or <see langword="null"/> when it asked for
/// none.
/// <para>
/// A map rather than more members, because which channels are here is the caller's choice: a sample
/// has a hundred and seventy-five and the fields above are the fifteen every chart starts from. A
/// channel the simulator did not report is absent rather than null — the same rule the rest of this
/// wire follows.
/// </para>
/// </param>
public sealed record TelemetrySampleResponse(
    long SequenceNumber,
    DateTimeOffset TimestampUtc,
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
    float? TrackPositionFraction,
    IReadOnlyDictionary<string, object?>? Channels = null);

/// <summary>The samples recorded for one lap, in the order they were captured.</summary>
/// <param name="LapNumber">The lap read.</param>
/// <param name="Samples">Samples ordered by <see cref="TelemetrySampleResponse.SequenceNumber"/>.</param>
public sealed record LapSamplesResponse(
    int LapNumber,
    IReadOnlyList<TelemetrySampleResponse> Samples);

/// <summary>The samples recorded for the laps a request named.</summary>
/// <remarks>
/// <b>Keyed by lap even when one lap was asked for.</b> An overlay of two to four laps is the normal
/// way stored telemetry is read, so the shape that carries several is the shape, and a caller
/// charting one lap indexes into a list of one rather than meeting a second response type.
/// </remarks>
/// <param name="SessionId">The session read.</param>
/// <param name="Laps">One entry per requested lap, ascending by <see cref="LapSamplesResponse.LapNumber"/>.</param>
public sealed record TelemetryResponse(
    Guid SessionId,
    IReadOnlyList<LapSamplesResponse> Laps);

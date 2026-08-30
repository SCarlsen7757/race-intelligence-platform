namespace RaceIntelligence.Core.Sessions;

/// <summary>How a car is being served by its pit crew, as reported by the simulator.</summary>
public enum PitStopStatus
{
    /// <summary>The simulator does not report a pit stop status for this car.</summary>
    Unavailable = -1,

    /// <summary>A stop is under way with two tyres still unserved.</summary>
    TwoTyresUnserved = 0,

    /// <summary>A stop is under way with four tyres still unserved.</summary>
    FourTyresUnserved = 1,

    /// <summary>The stop has been served.</summary>
    Served = 2,
}

/// <summary>Where a car is in the act of pitting, from the track to the box and back out.</summary>
/// <remarks>
/// <para>
/// A ladder rather than a boolean because "in the pit lane" is four different things to a race
/// engineer: a car that has just committed, one sitting on the jacks, and one rejoining are each a
/// different number of seconds away from being back in the fight.
/// </para>
/// <para>
/// <b>Not every rung is knowable for every car.</b> Simulators publish the full progression for the
/// car being driven locally and, typically, a bare in-pit-lane flag for everyone else. A connector
/// that can only tell the pit lane from the track reports <see cref="InPitLane"/> — which is a
/// weaker claim than <see cref="Entering"/>, not a synonym for it, and must not be rendered as one.
/// </para>
/// <para>
/// The 0-4 values line up with RaceRoom's <c>pit_state</c> by construction, since that connector is
/// the only one so far and a translation table with one entry is a translation table nobody reads.
/// <see cref="InPitLane"/> is this platform's own addition and has no RaceRoom counterpart.
/// </para>
/// </remarks>
public enum PitLaneState
{
    /// <summary>The simulator does not report where this car is relative to the pit lane.</summary>
    Unavailable = -1,

    /// <summary>On track, with no stop under way.</summary>
    OnTrack = 0,

    /// <summary>A stop has been requested; the car is still on track.</summary>
    Requested = 1,

    /// <summary>In the pit lane, heading for the box.</summary>
    Entering = 2,

    /// <summary>Stationary in the pit box.</summary>
    Stopped = 3,

    /// <summary>In the pit lane, heading back out.</summary>
    Exiting = 4,

    /// <summary>
    /// In the pit lane, at a stage the simulator does not report and the connector cannot infer.
    /// </summary>
    InPitLane = 5,
}

/// <summary>How a car's race ended, as reported by the simulator.</summary>
public enum DriverFinishStatus
{
    /// <summary>The simulator does not report a finish status for this car.</summary>
    Unavailable = -1,

    /// <summary>Still running.</summary>
    None = 0,

    Finished = 1,
    DidNotFinish = 2,
    DidNotQualify = 3,
    DidNotStart = 4,
    Disqualified = 5,
}

/// <summary>
/// One car's scoring and timing state at a moment in a session — the simulator-independent view of
/// a single row in a timing tower.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately the <b>observable</b> subset: what a simulator publishes about every car in
/// the session, including cars nobody is collecting from. It is scoring-granularity by nature —
/// position, lap and sector times, gaps, pit state — and carries no pedal inputs, tyre pressures,
/// tyre wear, fuel or damage. Those exist only for the car the simulator is running locally, and
/// travel in <see cref="RaceIntelligence.Collector.Abstractions.Telemetry.TelemetrySample"/> instead.
/// </para>
/// <para>
/// That asymmetry is intentional and load-bearing: when several people in the same online session
/// each run a collector, each one contributes a rich local view of its own car plus a thin
/// observed view of everyone else. Merging those views is what turns a timing tower into a
/// telemetry tower for the drivers who are participating.
/// </para>
/// </remarks>
public sealed record DriverStanding
{
    /// <summary>
    /// The simulator's stable identifier for the human or AI driving this car, when it has one.
    /// </summary>
    /// <remarks>
    /// This is the join key across collectors: a driver's own machine reports this id for itself,
    /// and every other machine in the same session reports the same id for that driver. Prefer it
    /// over <see cref="SlotId"/> and <see cref="DisplayName"/>, both of which are only unique
    /// within a single session and a single machine's view of it respectively.
    /// </remarks>
    public string? SimDriverId { get; init; }

    /// <summary>
    /// The simulator's per-session slot for this car. Unique within a session but reused across
    /// sessions, so it is a fallback join key rather than an identity.
    /// </summary>
    public int? SlotId { get; init; }

    /// <summary>The driver's name as the simulator publishes it.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The car number, when the simulator reports one.</summary>
    public int? CarNumber { get; init; }

    /// <summary>Simulator-specific car model id, as a string. See <see cref="SessionInfo.SimCarId"/>.</summary>
    public string? SimCarId { get; init; }

    /// <summary>Simulator-specific car class id, as a string.</summary>
    public string? SimCarClassId { get; init; }

    /// <summary>Simulator-specific manufacturer id, as a string.</summary>
    public string? SimManufacturerId { get; init; }

    /// <summary>Overall position in the session, 1-based.</summary>
    public int? Position { get; init; }

    /// <summary>Position within the car's class, 1-based.</summary>
    public int? PositionInClass { get; init; }

    /// <summary>
    /// Laps fully completed. <see langword="null"/> when the simulator does not report it — which
    /// is not the same as zero.
    /// </summary>
    /// <remarks>
    /// Nullable for the same reason every other unreported field here is: a car in a slot that is
    /// not yet active reports "unknown", and recording that as the fact "has completed no laps"
    /// makes it indistinguishable from a car sitting on the grid. On a timing tower that is the
    /// difference between a driver who is out and one who has not started.
    /// </remarks>
    public int? CompletedLaps { get; init; }

    /// <summary>Progress around the current lap, 0..1.</summary>
    public float? TrackPositionFraction { get; init; }

    /// <summary>The sector the car is currently in, 1-based.</summary>
    public int? Sector { get; init; }

    /// <summary>Current speed in meters per second.</summary>
    public float? Speed { get; init; }

    /// <summary>Elapsed time on the lap in progress.</summary>
    public TimeSpan? CurrentLapTime { get; init; }

    /// <summary>The most recently completed lap's time.</summary>
    public TimeSpan? PreviousLapTime { get; init; }

    /// <summary>The car's best lap time so far this session.</summary>
    public TimeSpan? BestLapTime { get; init; }

    /// <summary>Whether the lap in progress is still valid.</summary>
    public bool? CurrentLapValid { get; init; }

    /// <summary>
    /// Cumulative split times for the lap in progress. Index 0 is the time at the end of sector 1,
    /// index 1 at the end of sector 2, and so on; entries for sectors not yet reached are
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Cumulative rather than per-sector because that is the form simulators publish. Callers
    /// wanting an individual sector's duration subtract the previous entry — doing that conversion
    /// here would lose information when an earlier split is missing.
    /// </remarks>
    public IReadOnlyList<TimeSpan?> CurrentSectorTimes { get; init; } = [];

    /// <summary>Cumulative split times for the most recently completed lap. See <see cref="CurrentSectorTimes"/>.</summary>
    public IReadOnlyList<TimeSpan?> PreviousSectorTimes { get; init; } = [];

    /// <summary>Cumulative split times for the car's best lap this session. See <see cref="CurrentSectorTimes"/>.</summary>
    public IReadOnlyList<TimeSpan?> BestSectorTimes { get; init; } = [];

    /// <summary>Gap to the car ahead on track, when the simulator reports one.</summary>
    public TimeSpan? GapToCarAhead { get; init; }

    /// <summary>Gap to the car behind on track, when the simulator reports one.</summary>
    public TimeSpan? GapToCarBehind { get; init; }

    /// <summary>Whether the car is currently in the pit lane.</summary>
    public bool? InPitLane { get; init; }

    /// <summary>
    /// Where the car is in the act of pitting — the graded version of <see cref="InPitLane"/>.
    /// </summary>
    /// <remarks>
    /// Kept alongside the boolean rather than replacing it, because the two answer different
    /// questions and one of them is always cheap and always true. <see cref="InPitLane"/> is what a
    /// gap calculation and a row's dimming want; this is what a strategist reading the tower wants.
    /// A connector that cannot grade the stage still reports <see cref="PitLaneState.InPitLane"/>
    /// here, so a consumer never has to fall back to reading the boolean itself.
    /// </remarks>
    public PitLaneState PitLaneState { get; init; } = PitLaneState.Unavailable;

    /// <summary>Progress of the current pit stop, when one is under way.</summary>
    public PitStopStatus PitStopStatus { get; init; } = PitStopStatus.Unavailable;

    /// <summary>Number of pit stops completed this session.</summary>
    public int? PitStopCount { get; init; }

    /// <summary>How the car's session ended, or <see cref="DriverFinishStatus.None"/> while running.</summary>
    public DriverFinishStatus FinishStatus { get; init; } = DriverFinishStatus.Unavailable;

    /// <summary>
    /// How many distinct penalty types are currently outstanding, when the simulator reports
    /// penalties at all.
    /// </summary>
    /// <remarks>
    /// Types, not penalties: a simulator that reports pending penalties as a fixed set of slots
    /// (drive-through, stop-and-go, ...) can say which kinds are pending but not how many of each.
    /// Zero therefore means "nothing pending" and is a real answer, distinct from
    /// <see langword="null"/>.
    /// </remarks>
    public int? PenaltyCount { get; init; }
}

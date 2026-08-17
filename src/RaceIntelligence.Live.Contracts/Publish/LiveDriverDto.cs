using MessagePack;

namespace RaceIntelligence.Live.Contracts.Publish;

/// <summary>
/// Wire form of <see cref="RaceIntelligence.Core.Sessions.DriverStanding"/> — one car's scoring and
/// timing state, as one client observes it.
/// </summary>
/// <remarks>
/// <para>
/// Sector times stay variable-length rather than being flattened into
/// <c>Sector1</c>/<c>Sector2</c>/<c>Sector3</c> members the way the ingest contracts flatten
/// per-wheel groups. Wheels are four everywhere; sectors are three in RaceRoom and not
/// three by definition, so flattening would silently truncate the first simulator that disagrees.
/// The arrays carry cumulative splits from the start of the lap, with <see langword="null"/> for a
/// sector not yet reached — the same convention as the Core type.
/// </para>
/// <para>
/// Times are <see cref="TimeSpan"/> rather than seconds-as-float: MessagePack encodes them
/// natively and exactly, so a value survives the round trip unchanged. The browser-facing contracts
/// convert to milliseconds separately, where a JSON number is the more natural form.
/// </para>
/// </remarks>
[MessagePackObject]
public sealed record LiveDriverDto
{
    /// <summary>The simulator's stable driver identity, when it reports one. The join key across clients.</summary>
    [Key(0)]
    public string? SimDriverId { get; init; }

    /// <summary>The simulator's per-session slot. Fallback join key when there is no driver identity.</summary>
    [Key(1)]
    public int? SlotId { get; init; }

    /// <summary>The driver's name as the simulator publishes it.</summary>
    [Key(2)]
    public required string DisplayName { get; init; }

    /// <summary>Car number, when reported.</summary>
    [Key(3)]
    public int? CarNumber { get; init; }

    /// <summary>Simulator-specific car model id.</summary>
    [Key(4)]
    public string? SimCarId { get; init; }

    /// <summary>Simulator-specific car class id.</summary>
    [Key(5)]
    public string? SimCarClassId { get; init; }

    /// <summary>Simulator-specific manufacturer id.</summary>
    [Key(6)]
    public string? SimManufacturerId { get; init; }

    /// <summary>Overall position, 1-based.</summary>
    [Key(7)]
    public int? Position { get; init; }

    /// <summary>Position within class, 1-based.</summary>
    [Key(8)]
    public int? PositionInClass { get; init; }

    /// <summary>Laps fully completed. <see langword="null"/> when the simulator does not report it.</summary>
    [Key(9)]
    public int? CompletedLaps { get; init; }

    /// <summary>Progress around the current lap, 0..1.</summary>
    [Key(10)]
    public float? TrackPositionFraction { get; init; }

    /// <summary>Current sector, 1-based.</summary>
    [Key(11)]
    public int? Sector { get; init; }

    /// <summary>Speed in meters per second.</summary>
    [Key(12)]
    public float? Speed { get; init; }

    /// <summary>Elapsed time on the lap in progress.</summary>
    [Key(13)]
    public TimeSpan? CurrentLapTime { get; init; }

    /// <summary>The most recently completed lap's time.</summary>
    [Key(14)]
    public TimeSpan? PreviousLapTime { get; init; }

    /// <summary>Best lap time so far this session.</summary>
    [Key(15)]
    public TimeSpan? BestLapTime { get; init; }

    /// <summary>Whether the lap in progress is still valid.</summary>
    [Key(16)]
    public bool? CurrentLapValid { get; init; }

    /// <summary>Cumulative splits for the lap in progress. <see langword="null"/> when not carried.</summary>
    /// <remarks>
    /// Nullable, and deliberately without an <c>= []</c> initializer. MessagePack assigns
    /// <c>init</c> properties after construction, so a message that omits this key leaves the
    /// initializer overwritten with <see langword="null"/> anyway — an empty-looking default that is
    /// really a null reference waiting to be indexed. Declaring the absence honestly and coalescing
    /// once, in <c>LiveStandingsContractMapper</c>, is the version that cannot surprise anyone.
    /// </remarks>
    [Key(17)]
    public IReadOnlyList<TimeSpan?>? CurrentSectorTimes { get; init; }

    /// <summary>Cumulative splits for the most recently completed lap. See <see cref="CurrentSectorTimes"/>.</summary>
    [Key(18)]
    public IReadOnlyList<TimeSpan?>? PreviousSectorTimes { get; init; }

    /// <summary>Cumulative splits for the best lap this session. See <see cref="CurrentSectorTimes"/>.</summary>
    [Key(19)]
    public IReadOnlyList<TimeSpan?>? BestSectorTimes { get; init; }

    /// <summary>Gap to the car ahead on track.</summary>
    [Key(20)]
    public TimeSpan? GapToCarAhead { get; init; }

    /// <summary>Gap to the car behind on track.</summary>
    [Key(21)]
    public TimeSpan? GapToCarBehind { get; init; }

    /// <summary>Whether the car is in the pit lane.</summary>
    [Key(22)]
    public bool? InPitLane { get; init; }

    /// <summary>
    /// Where the car is in the act of pitting, as
    /// <see cref="RaceIntelligence.Core.Sessions.PitLaneState"/>.
    /// </summary>
    [Key(27)]
    public required int PitLaneState { get; init; }

    /// <summary>Pit stop progress, as <see cref="RaceIntelligence.Core.Sessions.PitStopStatus"/>.</summary>
    [Key(23)]
    public required int PitStopStatus { get; init; }

    /// <summary>Pit stops completed.</summary>
    [Key(24)]
    public int? PitStopCount { get; init; }

    /// <summary>How the car's session ended, as <see cref="RaceIntelligence.Core.Sessions.DriverFinishStatus"/>.</summary>
    [Key(25)]
    public required int FinishStatus { get; init; }

    /// <summary>Penalties currently outstanding.</summary>
    [Key(26)]
    public int? PenaltyCount { get; init; }
}

namespace RaceIntelligence.Core.Sessions;

/// <summary>What a race's length is measured in.</summary>
/// <remarks>
/// Explicit for the same reason <see cref="PitWindowUnit"/> is, and the hazard is if anything
/// sharper here. A race is run either to a lap count or to a clock, and a consumer that guessed
/// wrong would not merely mislabel a number — it would divide fuel by the wrong thing entirely and
/// report a car as comfortably inside its stint when it is two laps short.
/// </remarks>
public enum RaceLengthUnit
{
    /// <summary>The simulator did not say, so neither figure can be trusted to govern.</summary>
    Unknown = 0,

    /// <summary>The race ends after a fixed number of laps.</summary>
    Laps = 1,

    /// <summary>The race ends after a fixed duration.</summary>
    Time = 2,
}

/// <summary>
/// How long the race is: a lap count, a duration, and which of the two actually ends it.
/// </summary>
/// <remarks>
/// <para>
/// Both figures travel together rather than being collapsed into one, because a simulator may
/// report both and they answer different questions. <see cref="Unit"/> says which one ends the race;
/// the other is context, not an alternative.
/// </para>
/// <para>
/// Lives on <see cref="SessionStandings"/> beside <see cref="PitWindow"/> rather than on
/// <see cref="SessionInfo"/>. It is static for a session and could have gone on the announcement,
/// but the announcement is made once at the start — and RaceRoom reports a lap count of <c>-1</c>
/// until the session is actually running, so an announcement-time read would frequently carry
/// nothing and never be corrected.
/// </para>
/// <para>
/// Both members are nullable and the connector translates its own sentinel. RaceRoom writes
/// <c>-1</c> in whichever field does not apply, and a strategist told the race is negative one laps
/// long is worse served than one told it is unknown.
/// </para>
/// </remarks>
public sealed record RaceLength
{
    /// <summary>Total laps, or <see langword="null"/> when the session is not run to a lap count.</summary>
    public int? Laps { get; init; }

    /// <summary>Total session duration in seconds, or <see langword="null"/> when not run to a clock.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Which of the two ends the race.</summary>
    public RaceLengthUnit Unit { get; init; } = RaceLengthUnit.Unknown;

    /// <summary>
    /// Whether this describes a length anything can be computed from.
    /// </summary>
    /// <remarks>
    /// A unit without the matching figure is not a length. Folded here so every consumer answers it
    /// the same way, exactly as <see cref="PitWindow.Exists"/> does for a window — and for the same
    /// reason, since "the simulator said nothing" and "this session has no fixed length" are
    /// different facts that a fuel readout has to treat identically.
    /// </remarks>
    public bool Exists => Unit switch
    {
        RaceLengthUnit.Laps => Laps is not null,
        RaceLengthUnit.Time => DurationSeconds is not null,
        _ => false,
    };
}

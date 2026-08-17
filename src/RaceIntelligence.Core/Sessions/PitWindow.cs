namespace RaceIntelligence.Core.Sessions;

/// <summary>Whether a session's mandatory pit window is currently accepting stops.</summary>
/// <remarks>
/// <para>
/// A property of the <b>session</b>, not of a car: every driver in a race shares one window. The
/// per-car half of pitting is <see cref="PitLaneState"/> and <see cref="PitStopStatus"/>, which say
/// where one car is in the act of stopping; this says whether stopping counts yet.
/// </para>
/// <para>
/// The values line up with RaceRoom's <c>pit_window</c> by construction, for the same reason
/// <see cref="PitLaneState"/> lines up with its <c>pit_state</c>: it is the only connector so far,
/// and a translation table with one entry is a translation table nobody reads.
/// </para>
/// </remarks>
public enum PitWindowStatus
{
    /// <summary>The simulator does not report a pit window for this session.</summary>
    Unavailable = -1,

    /// <summary>This session has no mandatory pit window at all.</summary>
    Disabled = 0,

    /// <summary>
    /// A window exists but is not open — which covers both "not open yet" and "already gone by".
    /// </summary>
    /// <remarks>
    /// The simulator does not distinguish the two, and the distinction matters enormously to a
    /// strategist, so a consumer that wants it must derive it from
    /// <see cref="PitWindow.Start"/>/<see cref="PitWindow.End"/> against the session's own progress
    /// rather than reading it off this value.
    /// </remarks>
    Closed = 1,

    /// <summary>The window is open and a stop taken now counts.</summary>
    Open = 2,

    /// <summary>The local car is stopped in its box during the window.</summary>
    Stopped = 3,

    /// <summary>The local car has served its mandatory stop.</summary>
    Completed = 4,
}

/// <summary>What <see cref="PitWindow.Start"/> and <see cref="PitWindow.End"/> are measured in.</summary>
/// <remarks>
/// Explicit rather than inferred by the consumer. Simulators express the window in whatever unit the
/// session is run in — a lap number in a lap race, a minute in a timed one — and the same integer
/// <c>25</c> is either lap 25 or the 25-minute mark. A dashboard that guessed wrong would put a
/// mandatory stop most of an hour away from where it actually is.
/// </remarks>
public enum PitWindowUnit
{
    /// <summary>The simulator did not say, so neither bound can be labelled.</summary>
    Unknown = 0,

    /// <summary>Lap numbers.</summary>
    Laps = 1,

    /// <summary>Minutes of session time elapsed.</summary>
    Minutes = 2,
}

/// <summary>
/// A session's mandatory pit window: whether it is open, and the bounds it runs between.
/// </summary>
/// <remarks>
/// Lives on <see cref="SessionStandings"/> rather than on <see cref="SessionInfo"/> because
/// <see cref="Status"/> moves during a race — closed, then open, then completed — and
/// <see cref="SessionInfo"/> is the answer to "what session is this" made once at the start. The
/// bounds are static for the session and travel with the status only because they are meaningless
/// apart from it.
/// </remarks>
public sealed record PitWindow
{
    /// <summary>Whether the window is currently accepting stops.</summary>
    public PitWindowStatus Status { get; init; } = PitWindowStatus.Unavailable;

    /// <summary>
    /// Where the window opens, in <see cref="Unit"/>, or <see langword="null"/> when unreported.
    /// </summary>
    /// <remarks>
    /// Null rather than the simulator's own sentinel. RaceRoom writes <c>-1</c> for "not available",
    /// and a banner rendering that verbatim would announce a pit window opening on lap −1.
    /// </remarks>
    public int? Start { get; init; }

    /// <summary>Where the window closes, in <see cref="Unit"/>. See <see cref="Start"/>.</summary>
    public int? End { get; init; }

    /// <summary>What <see cref="Start"/> and <see cref="End"/> are counted in.</summary>
    public PitWindowUnit Unit { get; init; } = PitWindowUnit.Unknown;

    /// <summary>
    /// Whether this describes a window worth showing at all.
    /// </summary>
    /// <remarks>
    /// A session the simulator says nothing about and one that has no window are different facts and
    /// the same UI: nothing. Both are folded here so every consumer answers them the same way,
    /// instead of each remembering to check two cases.
    /// </remarks>
    public bool Exists => Status is not (PitWindowStatus.Unavailable or PitWindowStatus.Disabled);
}

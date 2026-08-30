namespace RaceIntelligence.Connectors.RaceRoom;

/// <summary>Tuning knobs for <see cref="RaceRoomTelemetrySource"/>.</summary>
public sealed record RaceRoomConnectorOptions
{
    /// <summary>How often to poll the shared memory block while connected. Default: 60 Hz.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>
    /// How long to wait between attempts to (re)open the shared memory block while
    /// disconnected or waiting for the simulator to start.
    /// </summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the simulation tick counter may stay frozen while in a session before the game is
    /// presumed to have exited (or crashed). This is the connector's only liveness signal: the
    /// shared memory section outlives the RaceRoom process, so a dead game looks exactly like a
    /// live one that stopped writing. Must comfortably exceed the longest legitimate freeze (an
    /// alt-tab/loading stall), hence a default well above a paused frame or two.
    /// </summary>
    public TimeSpan StaleFrameTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a session may stay suspended (driver in an in-session menu, or the game paused)
    /// before it is ended anyway.
    /// </summary>
    /// <remarks>
    /// Suspension exists so a menu visit does not fragment one session into several, so this has to
    /// comfortably exceed a realistic pause — adjusting a setup, answering the door. It is also the
    /// only liveness check that applies while suspended: a paused simulator freezes
    /// <c>game_simulation_ticks</c>, so <see cref="StaleFrameTimeout"/> cannot run at the same time
    /// without declaring every long pause a crashed game.
    /// </remarks>
    public TimeSpan MaxSuspendDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a session's identifying tuple must stay unchanged before a session is considered
    /// started.
    /// </summary>
    /// <remarks>
    /// The game briefly publishes the next session's type while it is still loading, which produced
    /// real sessions lasting a fraction of a second and holding a handful of samples. Waiting for
    /// the tuple to settle discards those without needing to know the loading sequence. Set to
    /// <see cref="TimeSpan.Zero"/> to start on the first qualifying frame.
    /// </remarks>
    public TimeSpan SessionStartDebounce { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often to re-read the whole field's scoring data — the source of
    /// <see cref="RaceIntelligence.Collector.Abstractions.Telemetry.StandingsUpdated"/>. Default: 10 Hz.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately much slower than <see cref="PollInterval"/>. The driver array is 328 bytes per
    /// car, so a full grid is a far larger read than the player snapshot, and every field in it —
    /// position, lap and sector times, gaps, pit state — changes on the scale of seconds. Reading
    /// it at the sample rate would multiply the connector's memory traffic for data that could not
    /// possibly differ between consecutive frames.
    /// </para>
    /// <para>
    /// Set to <see cref="TimeSpan.Zero"/> to read it on every poll, or to
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable standings entirely.
    /// </para>
    /// </remarks>
    public TimeSpan StandingsInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How often to re-publish the local car's simulator-specific channels — the source of
    /// <see cref="RaceIntelligence.Collector.Abstractions.Telemetry.SlowChannelsUpdated"/>. Default: 1 Hz.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slower again than <see cref="StandingsInterval"/>, and for a different reason. The document
    /// costs nothing extra to produce — the sample already carries it — but everything downstream of
    /// it is a JSON parse, and the values inside move on the scale of a whole race: damage after
    /// contact, push-to-pass once a lap. Publishing it at the sample rate would spend the
    /// dashboard's parsing budget watching numbers that do not change.
    /// </para>
    /// <para>
    /// Set to <see cref="TimeSpan.Zero"/> to publish on every poll, or to
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable extras entirely.
    /// </para>
    /// </remarks>
    public TimeSpan SlowChannelInterval { get; init; } = TimeSpan.FromSeconds(1);
}

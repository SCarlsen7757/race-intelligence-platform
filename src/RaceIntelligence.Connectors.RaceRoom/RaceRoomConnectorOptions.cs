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
}

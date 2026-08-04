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
}

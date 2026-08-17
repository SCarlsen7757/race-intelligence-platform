namespace RaceIntelligence.Core.Sessions;

/// <summary>
/// A snapshot of every car the simulator reports in a session, captured at one instant.
/// </summary>
/// <remarks>
/// Whole-snapshot rather than per-car deltas: simulators publish the full field every frame, and a
/// snapshot is the only form in which positions and gaps are internally consistent. Consumers that
/// want deltas compute them from consecutive snapshots, where they can see what actually changed.
/// </remarks>
public sealed record SessionStandings
{
    /// <summary>The session these standings belong to.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>UTC time the snapshot was taken, as observed by the connector.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>
    /// The simulator's own clock at capture, in seconds. Orders snapshots from one connector
    /// without depending on a machine's wall clock.
    /// </summary>
    public double? SimulationTime { get; init; }

    /// <summary>
    /// The <see cref="DriverStanding.SimDriverId"/> of the car this connector is collecting
    /// locally, when it can be identified.
    /// </summary>
    /// <remarks>
    /// Marks which row in <see cref="Drivers"/> the reporting machine has rich telemetry for. When
    /// snapshots from several machines are merged, this is what says whose view is authoritative
    /// for a given car.
    /// </remarks>
    public string? LocalSimDriverId { get; init; }

    /// <summary>Every car in the session, in the simulator's own order.</summary>
    public required IReadOnlyList<DriverStanding> Drivers { get; init; }
}

namespace RaceIntelligence.Core.Games;

/// <summary>
/// Records the exact combination of simulator build, telemetry API version, and connector
/// version that produced a session's data.
/// </summary>
/// <remarks>
/// Raw telemetry is never modified or deleted, so it has to stay interpretable years later. A sim
/// update can silently change units or redefine a field, and our own connector can have bugs that
/// get fixed. Recording all three components independently is what lets the platform later tell a
/// real driving anomaly from an upstream change, and exclude sessions from a known-bad connector
/// build, instead of silently mixing incompatible data.
/// </remarks>
public sealed record GameVersionIdentity
{
    /// <summary>Which simulator this version identity describes.</summary>
    public required GameIdentity Game { get; init; }

    /// <summary>
    /// The simulator's own build/version string, when it exposes one. <see langword="null"/> when
    /// the sim does not report a build identifier over its telemetry API.
    /// </summary>
    public string? GameVersion { get; init; }

    /// <summary>Major component of the telemetry API version reported by the simulator itself.</summary>
    public required int ApiVersionMajor { get; init; }

    /// <summary>Minor component of the telemetry API version reported by the simulator itself.</summary>
    public required int ApiVersionMinor { get; init; }

    /// <summary>
    /// The version of our own connector assembly that translated the simulator's raw telemetry
    /// into the canonical model. Distinct connector versions may translate the same sim data
    /// differently (bug fixes, new fields), so this must be recorded independently of
    /// <see cref="GameVersion"/> and the API version.
    /// </summary>
    public required string ConnectorVersion { get; init; }
}

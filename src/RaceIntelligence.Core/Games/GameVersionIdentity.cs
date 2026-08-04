namespace RaceIntelligence.Core.Games;

/// <summary>
/// Records the exact combination of simulator build, telemetry API version, and connector
/// version that produced a session's data.
/// </summary>
/// <remarks>
/// <para>
/// Raw telemetry is permanent — it is never modified or deleted, and must remain interpretable
/// years after it was collected. That is only possible if every session pins down exactly what
/// produced it: a simulator update can silently change units, add fields, or redefine the
/// meaning of an existing value, and our own connector code can have bugs that get fixed later.
/// Without this record, a future change anywhere in that chain would make historical data
/// ambiguous — the platform could no longer tell whether an anomaly is a real driving event or a
/// side effect of an upstream change.
/// </para>
/// <para>
/// Recording all three components independently means the platform can: detect when a game
/// update changed telemetry semantics (same game, new <see cref="GameVersion"/>), exclude
/// sessions collected by a connector with a known bug (bad <see cref="ConnectorVersion"/>), and
/// replay or recompute history correctly per version rather than silently mixing incompatible
/// data.
/// </para>
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

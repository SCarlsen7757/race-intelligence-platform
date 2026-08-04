namespace RaceIntelligence.Ingest.Contracts;

/// <summary>
/// Identifies the exact simulator build, telemetry API version, and connector version that
/// produced a session or batch — the wire form of <see cref="RaceIntelligence.Core.Games.GameVersionIdentity"/>.
/// </summary>
/// <remarks>
/// The backend is simulator-agnostic: nothing downstream of this DTO branches on
/// <see cref="GameKey"/>. It exists purely as reference data and provenance, per the platform
/// guiding principle that every session must record the game, game version, telemetry API version,
/// and connector version that produced it — see the README's "Game Versions" section for why this
/// must remain interpretable indefinitely.
/// </remarks>
/// <param name="GameKey">Stable, lowercase, machine-readable simulator identifier (e.g. <c>"raceroom"</c>). Never changes once assigned.</param>
/// <param name="GameName">Human-readable simulator display name.</param>
/// <param name="GameVersion">The simulator's own build/version string, if it reports one. <see langword="null"/> if not exposed.</param>
/// <param name="ApiVersionMajor">Major component of the telemetry API version reported by the simulator itself.</param>
/// <param name="ApiVersionMinor">Minor component of the telemetry API version reported by the simulator itself.</param>
/// <param name="ConnectorVersion">The version of the connector assembly that translated the simulator's raw telemetry into the canonical model.</param>
public sealed record GameVersionDto(
    string GameKey,
    string GameName,
    string? GameVersion,
    int ApiVersionMajor,
    int ApiVersionMinor,
    string ConnectorVersion);

namespace RaceIntelligence.Persistence.Entities;

/// <summary>
/// A distinct combination of simulator build, telemetry API version, and connector version — the
/// exact provenance every <see cref="Session"/> is pinned to.
/// </summary>
/// <remarks>
/// Raw telemetry is permanent, so a session's data must remain interpretable long after the game,
/// its telemetry API, or the connector that translated it have all moved on. Recording the three
/// components independently (rather than collapsing them into one string) lets the platform detect
/// when a game update changed telemetry semantics, exclude sessions collected by a known-buggy
/// connector build, and replay history correctly per version. See
/// <see cref="RaceIntelligence.Core.Games.GameVersionIdentity"/> for the full rationale — this
/// entity is its persisted form.
/// </remarks>
public sealed class GameVersion
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>The game this version belongs to.</summary>
    public Guid GameId { get; set; }

    public Game? Game { get; set; }

    /// <summary>
    /// The simulator's own build/version string, when it exposes one. <see langword="null"/> when
    /// the sim does not report a build identifier over its telemetry API.
    /// </summary>
    public string? GameVersionText { get; set; }

    /// <summary>Major component of the telemetry API version reported by the simulator itself.</summary>
    public int ApiVersionMajor { get; set; }

    /// <summary>Minor component of the telemetry API version reported by the simulator itself.</summary>
    public int ApiVersionMinor { get; set; }

    /// <summary>
    /// The version of our own connector assembly that translated the simulator's raw telemetry
    /// into the canonical model.
    /// </summary>
    public required string ConnectorVersion { get; set; }

    /// <summary>UTC time this exact combination was first observed.</summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>Sessions recorded under this exact game/API/connector combination.</summary>
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}

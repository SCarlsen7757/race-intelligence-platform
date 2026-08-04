namespace RaceIntelligence.Core.Games;

/// <summary>
/// Identifies a racing simulator as reference data — a stable key plus a human-readable name.
/// </summary>
/// <remarks>
/// This is reference data, not a branch in the code. Storing "which simulator produced this
/// session" lets the platform display and query by game, but no code path should ever inspect
/// <see cref="Key"/> to change analysis behavior; that decision belongs to
/// <see cref="RaceIntelligence.Core.Capabilities.SimCapabilities"/> instead.
/// </remarks>
/// <param name="Key">Stable, lowercase, machine-readable identifier (e.g. <c>"raceroom"</c>). Never changes once assigned.</param>
/// <param name="Name">Human-readable display name (e.g. <c>"RaceRoom Racing Experience"</c>).</param>
public sealed record GameIdentity(string Key, string Name);

/// <summary>
/// Well-known <see cref="GameIdentity"/> reference values shipped with the platform.
/// </summary>
public static class WellKnownGames
{
    /// <summary>RaceRoom Racing Experience, the platform's initial (Phase 1) connector target.</summary>
    public static readonly GameIdentity RaceRoom = new("raceroom", "RaceRoom Racing Experience");
}

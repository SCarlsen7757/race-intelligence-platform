namespace RaceIntelligence.Persistence.Entities;

/// <summary>
/// Persistence row for a racing simulator, keyed by a stable machine-readable
/// <see cref="Key"/> (e.g. <c>"raceroom"</c>).
/// </summary>
/// <remarks>
/// This is reference data, not a branch point. The rest of the schema never asks "is this
/// RaceRoom?" — see <see cref="RaceIntelligence.Core.Capabilities.SimCapabilities"/> for how the
/// platform keeps analysis simulator-agnostic instead.
/// </remarks>
public sealed class Game
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>Stable, lowercase, machine-readable identifier (e.g. <c>"raceroom"</c>). Unique.</summary>
    public required string Key { get; set; }

    /// <summary>Human-readable display name.</summary>
    public required string Name { get; set; }

    /// <summary>UTC time this game reference row was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The game builds / telemetry API / connector version combinations seen for this game.</summary>
    public ICollection<GameVersion> Versions { get; set; } = new List<GameVersion>();

    /// <summary>Tracks that belong to this game.</summary>
    public ICollection<Track> Tracks { get; set; } = new List<Track>();

    /// <summary>Cars that belong to this game.</summary>
    public ICollection<Car> Cars { get; set; } = new List<Car>();
}

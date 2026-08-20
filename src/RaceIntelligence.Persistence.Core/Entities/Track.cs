namespace RaceIntelligence.Persistence.Entities;

/// <summary>A track, scoped to the game it belongs to (track names are only unique per game).</summary>
public sealed class Track
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>Track name, unique within its game.</summary>
    public required string Name { get; set; }

    /// <summary>The layouts/configurations available on this track.</summary>
    public ICollection<TrackLayout> Layouts { get; set; } = new List<TrackLayout>();
}

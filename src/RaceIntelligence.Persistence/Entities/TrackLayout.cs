namespace RaceIntelligence.Persistence.Entities;

/// <summary>A specific layout/configuration of a <see cref="Track"/> (e.g. "Grand Prix", "National").</summary>
public sealed class TrackLayout
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>The track this layout belongs to.</summary>
    public Guid TrackId { get; set; }

    /// <summary>Navigation to the owning <see cref="Track"/>.</summary>
    public Track? Track { get; set; }

    /// <summary>Layout name, unique within its track.</summary>
    public required string Name { get; set; }

    /// <summary>Length of this layout, in meters.</summary>
    public double LengthMeters { get; set; }

    /// <summary>Sessions run on this layout.</summary>
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}

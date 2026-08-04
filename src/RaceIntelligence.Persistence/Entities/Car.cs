namespace RaceIntelligence.Persistence.Entities;

/// <summary>A car as identified by a specific game (car identity is only unique per game).</summary>
public sealed class Car
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>Car display name.</summary>
    public required string Name { get; set; }

    /// <summary>The car's manufacturer, if known.</summary>
    public Guid? ManufacturerId { get; set; }

    /// <summary>Navigation to the <see cref="Manufacturer"/>, if known.</summary>
    public Manufacturer? Manufacturer { get; set; }

    /// <summary>The car's class, if known.</summary>
    public Guid? CarClassId { get; set; }

    /// <summary>Navigation to the <see cref="CarClass"/>, if known.</summary>
    public CarClass? CarClass { get; set; }

    /// <summary>The game this car belongs to.</summary>
    public Guid GameId { get; set; }

    /// <summary>Navigation to the owning <see cref="Game"/>.</summary>
    public Game? Game { get; set; }

    /// <summary>The simulator's own internal identifier for this car. Unique within its game.</summary>
    public required string SimCarId { get; set; }

    /// <summary>Sessions driven in this car.</summary>
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}

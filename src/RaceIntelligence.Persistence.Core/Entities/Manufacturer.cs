namespace RaceIntelligence.Persistence.Entities;

/// <summary>A car manufacturer, shared reference data across all games.</summary>
public sealed class Manufacturer
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>Manufacturer name. Unique.</summary>
    public required string Name { get; set; }

    /// <summary>Cars made by this manufacturer.</summary>
    public ICollection<Car> Cars { get; set; } = new List<Car>();
}

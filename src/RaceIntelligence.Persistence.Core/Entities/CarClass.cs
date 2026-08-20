namespace RaceIntelligence.Persistence.Entities;

/// <summary>A car class/category (e.g. "GT3", "LMP2"), shared reference data across all games.</summary>
public sealed class CarClass
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>Class name. Unique.</summary>
    public required string Name { get; set; }

    /// <summary>Cars in this class.</summary>
    public ICollection<Car> Cars { get; set; } = new List<Car>();
}

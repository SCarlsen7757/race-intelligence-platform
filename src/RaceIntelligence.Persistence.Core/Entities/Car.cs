namespace RaceIntelligence.Persistence.Entities;

/// <summary>A car, as this simulator identifies it.</summary>
/// <remarks>
/// Identity is <see cref="SimCarId"/> alone, the same convention <see cref="Driver"/> follows.
/// It used to be scoped by game, because two simulators can hand out the same numeric id to
/// different cars — that scoping is gone because the *database* is the simulator now, so there is
/// no second simulator in here to collide with. See ADR 0001.
/// <para>
/// <see cref="Name"/> is <i>not</i> an identity — see its remarks.
/// </para>
/// </remarks>
public sealed class Car
{
    /// <summary>Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/>, never by the database.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The car's most recently observed display name. A mutable label, not an identity: a content
    /// update that renames a car rewrites this in place rather than creating a second row.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>The car's manufacturer, if known.</summary>
    public Guid? ManufacturerId { get; set; }

    public Manufacturer? Manufacturer { get; set; }

    /// <summary>The car's class, if known.</summary>
    public Guid? CarClassId { get; set; }

    public CarClass? CarClass { get; set; }

    /// <summary>
    /// The simulator's own internal identifier for this car. Unique within its game. Falls back to
    /// the reported name for sims that expose no car id, since the column is NOT NULL and something
    /// has to carry identity; such a car cannot survive a rename.
    /// </summary>
    public required string SimCarId { get; set; }

    /// <summary>Sessions driven in this car.</summary>
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}

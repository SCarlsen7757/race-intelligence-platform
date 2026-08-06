using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>Idempotent resolve-or-create access to <c>manufacturers</c>, <c>car_classes</c>, and <c>cars</c>.</summary>
/// <remarks>
/// Follows the same insert + unique-violation-retry pattern as <see cref="GameRepository"/> — see
/// <see cref="UniqueViolationDetection"/> for why.
/// </remarks>
/// <param name="db">The context to resolve/create through.</param>
public sealed class CarRepository(RaceIntelligenceDbContext db)
{
    /// <summary>Resolves or creates a manufacturer by name.</summary>
    public async Task<Manufacturer> ResolveOrCreateManufacturerAsync(string name, CancellationToken ct = default)
    {
        var existing = await db.Manufacturers.FirstOrDefaultAsync(m => m.Name == name, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var manufacturer = new Manufacturer { Id = Guid.CreateVersion7(), Name = name };
        db.Manufacturers.Add(manufacturer);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return manufacturer;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(manufacturer).State = EntityState.Detached;
            return await db.Manufacturers.SingleAsync(m => m.Name == name, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves or creates a car class by name.</summary>
    public async Task<CarClass> ResolveOrCreateCarClassAsync(string name, CancellationToken ct = default)
    {
        var existing = await db.CarClasses.FirstOrDefaultAsync(c => c.Name == name, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var carClass = new CarClass { Id = Guid.CreateVersion7(), Name = name };
        db.CarClasses.Add(carClass);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return carClass;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(carClass).State = EntityState.Detached;
            return await db.CarClasses.SingleAsync(c => c.Name == name, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the car identified by <paramref name="simCarId"/> within <paramref name="gameId"/> —
    /// or, when the sim supplies no id, by <paramref name="name"/> — creating the row (and its
    /// manufacturer/class) if this is the first time it has been seen. Returns
    /// <see langword="null"/> when neither an id nor a name is available, in which case the caller
    /// has nothing to attribute the session to and should leave <c>sessions.car_id</c> null.
    /// </summary>
    public async Task<Car?> ResolveOrCreateCarAsync(
        Guid gameId,
        string? simCarId,
        string? name,
        string? manufacturerName = null,
        string? carClassName = null,
        CancellationToken ct = default)
    {
        var hasSimId = !string.IsNullOrWhiteSpace(simCarId);
        var hasName = !string.IsNullOrWhiteSpace(name);

        if (!hasSimId && !hasName)
        {
            return null;
        }

        // cars.sim_car_id is NOT NULL and is the sole identity column, so a sim that reports only a
        // name has to be identified by that name. Such a car cannot survive a rename — the same
        // limit DriverRepository's name-only path documents, and for the same reason: the source
        // gave nothing stabler to key on.
        var identity = hasSimId ? simCarId! : name!;
        var existing = await FindCarAsync(gameId, identity, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // The rename case: the sim id is the identity, the name is just the latest label.
            if (hasName && existing.Name != name)
            {
                existing.Name = name!;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return existing;
        }

        Guid? manufacturerId = string.IsNullOrWhiteSpace(manufacturerName)
            ? null
            : (await ResolveOrCreateManufacturerAsync(manufacturerName, ct).ConfigureAwait(false)).Id;

        Guid? carClassId = string.IsNullOrWhiteSpace(carClassName)
            ? null
            : (await ResolveOrCreateCarClassAsync(carClassName, ct).ConfigureAwait(false)).Id;

        var car = new Car
        {
            Id = Guid.CreateVersion7(),
            // name is NOT NULL; when the sim gave an id but no name at all, the id stands in as the
            // label until a session reports a real one and the rename path above rewrites it.
            Name = hasName ? name! : identity,
            ManufacturerId = manufacturerId,
            CarClassId = carClassId,
            GameId = gameId,
            SimCarId = identity,
        };

        db.Cars.Add(car);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return car;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(car).State = EntityState.Detached;
            return await FindCarAsync(gameId, identity, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Unique-constraint violation on cars was reported, but the conflicting row could not be re-selected.");
        }
    }

    private Task<Car?> FindCarAsync(Guid gameId, string simCarId, CancellationToken ct) =>
        db.Cars.FirstOrDefaultAsync(c => c.GameId == gameId && c.SimCarId == simCarId, ct);
}

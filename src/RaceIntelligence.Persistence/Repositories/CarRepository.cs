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

    /// <summary>Resolves or creates a car by (game, sim car id), optionally resolving its manufacturer and class by name first.</summary>
    public async Task<Car> ResolveOrCreateCarAsync(
        Guid gameId,
        string simCarId,
        string name,
        string? manufacturerName = null,
        string? carClassName = null,
        CancellationToken ct = default)
    {
        var existing = await FindCarAsync(gameId, simCarId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        Guid? manufacturerId = manufacturerName is null
            ? null
            : (await ResolveOrCreateManufacturerAsync(manufacturerName, ct).ConfigureAwait(false)).Id;

        Guid? carClassId = carClassName is null
            ? null
            : (await ResolveOrCreateCarClassAsync(carClassName, ct).ConfigureAwait(false)).Id;

        var car = new Car
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            ManufacturerId = manufacturerId,
            CarClassId = carClassId,
            GameId = gameId,
            SimCarId = simCarId,
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
            return await FindCarAsync(gameId, simCarId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Unique-constraint violation on cars was reported, but the conflicting row could not be re-selected.");
        }
    }

    private Task<Car?> FindCarAsync(Guid gameId, string simCarId, CancellationToken ct) =>
        db.Cars.FirstOrDefaultAsync(c => c.GameId == gameId && c.SimCarId == simCarId, ct);
}

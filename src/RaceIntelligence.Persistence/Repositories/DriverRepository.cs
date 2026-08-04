using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>Resolve-or-create access to <c>drivers</c> by display name.</summary>
/// <remarks>
/// Unlike the other resolve-or-create repositories, the <c>drivers</c> table has no unique
/// constraint on <see cref="Driver.DisplayName"/> (see the entity's remarks) — a display name is
/// just a reported string, and two different real people may share one. This repository therefore
/// does a best-effort "find the first driver with this exact name, else create one"; it is not
/// race-safe the way <see cref="GameRepository"/> is, because there is no database constraint to
/// retry against. Two concurrent first-time resolutions of the same new name can legitimately
/// create two <see cref="Driver"/> rows. Callers that need strict per-name identity should
/// introduce and enforce that constraint explicitly — it is a product decision this schema
/// deliberately leaves open.
/// </remarks>
/// <param name="db">The context to resolve/create through.</param>
public sealed class DriverRepository(RaceIntelligenceDbContext db)
{
    /// <summary>Finds the first driver with the given display name, or creates one if none exists.</summary>
    public async Task<Driver> ResolveOrCreateAsync(string displayName, CancellationToken ct = default)
    {
        var existing = await db.Drivers.FirstOrDefaultAsync(d => d.DisplayName == displayName, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var driver = new Driver
        {
            Id = Guid.CreateVersion7(),
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Drivers.Add(driver);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return driver;
    }
}

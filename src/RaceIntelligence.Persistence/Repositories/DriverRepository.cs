using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>Resolve-or-create access to <c>drivers</c>, by the sim's own driver id where there is one.</summary>
/// <remarks>
/// Two resolution paths, both scoped to a game because sim driver ids share a numeric namespace
/// across sims:
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Sim id known</b> — the row is looked up by <c>(game_id, sim_driver_id)</c>, which is the
/// driver's real identity. A driver found this way under a new display name has renamed themselves,
/// and the stored <see cref="Driver.DisplayName"/> is updated in place rather than forking into a
/// second row. The name reported for each individual session is kept on
/// <see cref="Session.PlayerName"/>, so nothing is lost by overwriting it here.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Sim id unknown</b> — falls back to <c>(game_id, display_name)</c> among the rows that carry no
/// sim id. Such a driver cannot survive a rename; that is a limit of the source, not of this
/// schema.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>The two paths must be able to meet, or they reintroduce the very fork this schema exists to
/// prevent.</b> A driver row can legitimately exist with no sim id — every row predating the
/// migration that added the column does, and so does any session from a source that reported no id
/// at the time. If the sim-id path could only ever see rows that already carry an id, the first
/// session a pre-existing driver ran with an id would silently create a <i>second</i> row, and that
/// person's history would be split in exactly the way renaming used to split it. So each path also
/// looks across the boundary before inserting:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The sim-id path <b>adopts</b> a matching name-only row, stamping the id onto it, rather than
/// inserting alongside it.
/// </description>
/// </item>
/// <item>
/// <description>
/// The name-only path attaches to an existing id-carrying row when the name identifies exactly one,
/// rather than inserting a name-only twin of a driver already known by id.
/// </description>
/// </item>
/// </list>
/// <para>
/// Both crossings are matched on display name, so both inherit the fallback path's existing caveat:
/// two real people sharing one name within a game can be merged. That is the same trade already
/// accepted for name-based resolution, and it is strictly better than the guaranteed split it
/// replaces. Where the name is genuinely ambiguous — more than one driver in the game answering to
/// it — no guess is made and a separate row is kept instead.
/// </para>
/// Both paths are backed by a unique index (the second a partial one, filtered to
/// <c>sim_driver_id IS NULL</c>), so both use the same insert + unique-violation-retry pattern as
/// <see cref="GameRepository"/> and <see cref="CarRepository"/> — see
/// <see cref="UniqueViolationDetection"/> for why.
/// </remarks>
/// <param name="db">The context to resolve/create through.</param>
public sealed class DriverRepository(RaceIntelligenceDbContext db)
{
    /// <summary>
    /// Resolves the driver identified by <paramref name="simDriverId"/> within
    /// <paramref name="gameId"/> — or, when the sim supplies no id, by
    /// <paramref name="displayName"/> — creating the row if this is the first time it has been seen.
    /// Returns <see langword="null"/> when neither an id nor a name is available, in which case the
    /// caller has nothing to attribute the session to and should leave <c>sessions.driver_id</c> null.
    /// </summary>
    public async Task<Driver?> ResolveOrCreateAsync(
        Guid gameId,
        string? simDriverId,
        string? displayName,
        CancellationToken ct = default)
    {
        var hasSimId = !string.IsNullOrWhiteSpace(simDriverId);
        var hasName = !string.IsNullOrWhiteSpace(displayName);

        if (!hasSimId && !hasName)
        {
            return null;
        }

        return hasSimId
            ? await ResolveBySimIdAsync(gameId, simDriverId!, hasName ? displayName : null, ct).ConfigureAwait(false)
            : await ResolveByNameAsync(gameId, displayName!, ct).ConfigureAwait(false);
    }

    private async Task<Driver> ResolveBySimIdAsync(Guid gameId, string simDriverId, string? displayName, CancellationToken ct)
    {
        var existing = await FindBySimIdAsync(gameId, simDriverId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // The rename case: the sim id is the identity, the name is just the latest label.
            if (displayName is not null && existing.DisplayName != displayName)
            {
                existing.DisplayName = displayName;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return existing;
        }

        // Nothing carries this sim id yet. Before inserting, check whether this person is already
        // known by name alone — a row created before the sim id was ever recorded. Adopting it
        // keeps their history in one place; inserting alongside it would fork them permanently.
        if (displayName is not null)
        {
            var nameOnly = await FindByNameAsync(gameId, displayName, ct).ConfigureAwait(false);
            if (nameOnly is not null)
            {
                nameOnly.SimDriverId = simDriverId;
                try
                {
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return nameOnly;
                }
                catch (DbUpdateException ex) when (ex.IsUniqueViolation())
                {
                    // A concurrent resolution claimed this sim id first. Its row is the identity;
                    // abandon the adoption and use it, leaving the name-only row to be adopted (or
                    // not) by whoever it really belongs to.
                    await db.Entry(nameOnly).ReloadAsync(ct).ConfigureAwait(false);
                    return await FindBySimIdAsync(gameId, simDriverId, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            "Unique-constraint violation on drivers was reported while adopting a name-only row, but the conflicting row could not be re-selected.");
                }
            }
        }

        // display_name is NOT NULL; when the sim gave us an id but no name at all, the id stands in
        // as the label until a session reports a real one and the rename path above rewrites it.
        var driver = new Driver
        {
            Id = Guid.CreateVersion7(),
            GameId = gameId,
            SimDriverId = simDriverId,
            DisplayName = displayName ?? simDriverId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return await db.InsertRowAsync(driver, token => FindBySimIdAsync(gameId, simDriverId, token), "drivers", ct).ConfigureAwait(false);
    }

    private async Task<Driver> ResolveByNameAsync(Guid gameId, string displayName, CancellationToken ct)
    {
        var existing = await FindByNameAsync(gameId, displayName, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        // No name-only row. This person may still be known by a sim id from sessions where the
        // source did report one — attach to that row rather than growing a name-only twin beside
        // it. Two candidates are fetched so ambiguity can be detected: if the name identifies more
        // than one driver in this game there is nothing to distinguish them by, and guessing would
        // silently attribute a session to the wrong person. A separate row is the honest answer.
        var byName = await db.Drivers
            .Where(d => d.GameId == gameId && d.DisplayName == displayName)
            .OrderBy(d => d.CreatedAt)
            .Take(2)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (byName.Count == 1)
        {
            return byName[0];
        }

        var driver = new Driver
        {
            Id = Guid.CreateVersion7(),
            GameId = gameId,
            SimDriverId = null,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return await db.InsertRowAsync(driver, token => FindByNameAsync(gameId, displayName, token), "drivers", ct).ConfigureAwait(false);
    }

    private Task<Driver?> FindBySimIdAsync(Guid gameId, string simDriverId, CancellationToken ct) =>
        db.Drivers.FirstOrDefaultAsync(d => d.GameId == gameId && d.SimDriverId == simDriverId, ct);

    // The null literal (rather than a captured null variable) is deliberate: it is what makes EF
    // emit `sim_driver_id IS NULL`, matching the partial unique index's filter exactly.
    private Task<Driver?> FindByNameAsync(Guid gameId, string displayName, CancellationToken ct) =>
        db.Drivers.FirstOrDefaultAsync(
            d => d.GameId == gameId && d.SimDriverId == null && d.DisplayName == displayName, ct);
}

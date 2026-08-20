using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Identity.Entities;

namespace RaceIntelligence.Identity.Repositories;

/// <summary>
/// Reads and writes the identity registry.
/// </summary>
/// <remarks>
/// <b>Nothing here resolves-or-creates.</b> Every other repository in this platform does, because
/// every other repository is describing something a collector observed and the only sane response to
/// an unseen track is to record it. This one is describing a human assertion, and inventing a person
/// because a driver id turned up unclaimed would be the guessing that <c>PersonSimAlias</c> exists to
/// refuse. A caller asks; the registry answers, including "nobody has claimed this".
/// </remarks>
/// <param name="db">The identity store.</param>
public sealed class PersonRepository(IdentityDbContext db)
{
    /// <summary>Every person, with their aliases, ordered by name.</summary>
    /// <remarks>
    /// Unpaged on purpose. This is a hand-curated table of the humans someone cares to compare, not
    /// a driver list — it is measured in tens, and paging it would be machinery for a size it is not
    /// going to reach. Revisit if that stops being true.
    /// </remarks>
    public async Task<IReadOnlyList<Person>> ListAsync(CancellationToken ct = default) =>
        await db.People
            .AsNoTracking()
            .Include(p => p.Aliases)
            .OrderBy(p => p.DisplayName)
            .ThenBy(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>One person and their aliases, or <see langword="null"/> if no such person exists.</summary>
    public async Task<Person?> FindAsync(Guid id, CancellationToken ct = default) =>
        await db.People
            .AsNoTracking()
            .Include(p => p.Aliases)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

    /// <summary>Asserts a new person. Returns the created row; does not save.</summary>
    /// <remarks>
    /// Display names are not unique and this does not pretend otherwise — two humans genuinely share
    /// names, and quietly returning the existing row would merge two people on exactly the evidence
    /// this registry refuses to act on.
    /// </remarks>
    public Person Add(string displayName, DateTimeOffset now)
    {
        var person = new Person
        {
            Id = Guid.CreateVersion7(),
            DisplayName = displayName,
            CreatedAt = now,
        };

        db.People.Add(person);
        return person;
    }

    /// <summary>Claims one simulator identity for a person. Does not save.</summary>
    /// <remarks>
    /// The uniqueness of <c>(sim_key, sim_driver_id)</c> is left to the database rather than checked
    /// here: a read-then-write check is a race, and the index is the only thing that holds against
    /// two callers claiming the same id at once. The endpoint turns the resulting violation into a
    /// 409.
    /// </remarks>
    public PersonSimAlias AddAlias(Guid personId, string simKey, string simDriverId, DateTimeOffset now)
    {
        var alias = new PersonSimAlias
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            SimKey = simKey,
            SimDriverId = simDriverId,
            CreatedAt = now,
        };

        db.PersonSimAliases.Add(alias);
        return alias;
    }

    /// <summary>Releases one claim, so the simulator identity can be claimed again. Does not save.</summary>
    /// <returns><see langword="true"/> if a claim was found and removed.</returns>
    public async Task<bool> RemoveAliasAsync(Guid personId, string simKey, string simDriverId, CancellationToken ct = default)
    {
        var alias = await db.PersonSimAliases
            .FirstOrDefaultAsync(
                a => a.PersonId == personId && a.SimKey == simKey && a.SimDriverId == simDriverId,
                ct)
            .ConfigureAwait(false);

        if (alias is null)
        {
            return false;
        }

        db.PersonSimAliases.Remove(alias);
        return true;
    }

    /// <summary>
    /// Every simulator identity already claimed in one simulator.
    /// </summary>
    /// <remarks>
    /// The half of the unmapped-driver worklist this service can honestly answer. Working out which
    /// of a simulator's drivers nobody has claimed is a diff against that simulator's own
    /// <c>drivers</c> table — which this service cannot reach, by the same argument that gives the
    /// registry its own database. Whoever holds that table can ask for this set and do the diff.
    /// </remarks>
    public async Task<IReadOnlyList<PersonSimAlias>> ListAliasesAsync(string simKey, CancellationToken ct = default) =>
        await db.PersonSimAliases
            .AsNoTracking()
            .Where(a => a.SimKey == simKey)
            .OrderBy(a => a.SimDriverId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}

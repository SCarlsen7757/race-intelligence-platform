using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Identity.Repositories;
using RaceIntelligence.Identity.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Identity.Tests;

/// <summary>
/// The registry's rules, enforced where they have to be enforced: in the database.
/// </summary>
/// <remarks>
/// These assert against a real PostgreSQL container rather than the in-memory provider on purpose.
/// Every rule worth having here is a unique index or a foreign key, and the in-memory provider
/// honours neither — a suite that passed against it would be testing the C# and calling it a
/// constraint.
/// </remarks>
[Collection(IdentityPostgresCollection.Name)]
public sealed class IdentityRegistryTests(IdentityPostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private void SkipWithoutPostgres()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }
    }

    /// <summary>A unique suffix per test, so one container can host every case without cross-talk.</summary>
    private static string Unique() => Guid.CreateVersion7().ToString("N")[..12];

    [Fact]
    public async Task A_person_can_be_asserted_with_no_aliases_at_all()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);

        var person = people.Add($"Unmapped {Unique()}", Now);
        await db.SaveChangesAsync();

        var found = await people.FindAsync(person.Id);
        found.ShouldNotBeNull();
        found.Aliases.ShouldBeEmpty();
    }

    /// <summary>
    /// The point of the whole registry: one human, several simulators, tied together by nothing but
    /// a human having said so.
    /// </summary>
    [Fact]
    public async Task One_person_holds_identities_in_several_simulators()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var suffix = Unique();

        var person = people.Add($"Mark {suffix}", Now);
        people.AddAlias(person.Id, $"raceroom-{suffix}", "4242", Now);
        people.AddAlias(person.Id, $"iracing-{suffix}", "881109", Now);
        await db.SaveChangesAsync();

        var found = await people.FindAsync(person.Id);
        found.ShouldNotBeNull();
        found.Aliases.Select(a => a.SimDriverId).OrderBy(x => x).ShouldBe(["4242", "881109"]);
    }

    /// <summary>
    /// Two accounts in one simulator being the same human is ordinary, and the schema must be able
    /// to say so — the uniqueness runs the other way.
    /// </summary>
    [Fact]
    public async Task One_person_may_hold_two_identities_within_the_same_simulator()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var sim = $"raceroom-{Unique()}";

        var person = people.Add($"Two accounts {Unique()}", Now);
        people.AddAlias(person.Id, sim, "1", Now);
        people.AddAlias(person.Id, sim, "2", Now);

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// The constraint the registry turns on. Enforced by the index rather than by the endpoint,
    /// because the endpoint is not the only thing that will ever write here.
    /// </summary>
    [Fact]
    public async Task One_simulator_identity_cannot_belong_to_two_people()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var sim = $"raceroom-{Unique()}";

        var first = people.Add($"First {Unique()}", Now);
        people.AddAlias(first.Id, sim, "4242", Now);
        await db.SaveChangesAsync();

        var second = people.Add($"Second {Unique()}", Now);
        people.AddAlias(second.Id, sim, "4242", Now);

        var thrown = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        thrown.IsUniqueViolation().ShouldBeTrue();
    }

    /// <summary>
    /// The same numeric id in two simulators is two different humans as far as anyone can tell, and
    /// the registry must not collapse them. This is why the key is <c>(sim_key, sim_driver_id)</c>
    /// and not the id alone.
    /// </summary>
    [Fact]
    public async Task The_same_id_in_two_simulators_is_two_separate_claims()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var suffix = Unique();

        var first = people.Add($"RaceRoom person {suffix}", Now);
        people.AddAlias(first.Id, $"raceroom-{suffix}", "4242", Now);

        var second = people.Add($"iRacing person {suffix}", Now);
        people.AddAlias(second.Id, $"iracing-{suffix}", "4242", Now);

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    /// <summary>Display names are not identities, so two people are allowed to share one.</summary>
    [Fact]
    public async Task Two_people_may_share_a_display_name()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var name = $"Ambiguous {Unique()}";

        people.Add(name, Now);
        people.Add(name, Now);

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task An_alias_cannot_exist_without_a_person()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);

        people.AddAlias(Guid.CreateVersion7(), $"raceroom-{Unique()}", "4242", Now);

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// Releasing a claim has to actually free the simulator identity, or a mis-assertion is
    /// permanent — which on hand-curated state is the difference between a registry and a trap.
    /// </summary>
    [Fact]
    public async Task Releasing_a_claim_lets_someone_else_take_it()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var sim = $"raceroom-{Unique()}";

        var wrong = people.Add($"Wrong {Unique()}", Now);
        people.AddAlias(wrong.Id, sim, "4242", Now);
        await db.SaveChangesAsync();

        (await people.RemoveAliasAsync(wrong.Id, sim, "4242")).ShouldBeTrue();
        await db.SaveChangesAsync();

        var right = people.Add($"Right {Unique()}", Now);
        people.AddAlias(right.Id, sim, "4242", Now);

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    /// <summary>Deleting a person asserted by mistake must free every id they had claimed.</summary>
    [Fact]
    public async Task Deleting_a_person_releases_their_claims()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var sim = $"raceroom-{Unique()}";

        var person = people.Add($"Mistake {Unique()}", Now);
        people.AddAlias(person.Id, sim, "4242", Now);
        await db.SaveChangesAsync();

        db.People.Remove(await db.People.SingleAsync(p => p.Id == person.Id));
        await db.SaveChangesAsync();

        (await db.PersonSimAliases.CountAsync(a => a.SimKey == sim)).ShouldBe(0);
    }

    /// <summary>
    /// The half of the unmapped worklist this service can answer: which ids in one simulator are
    /// spoken for. Scoped to that simulator, or the diff on the other side would be against the
    /// wrong set.
    /// </summary>
    [Fact]
    public async Task Claimed_identities_are_listed_per_simulator()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var suffix = Unique();
        var raceroom = $"raceroom-{suffix}";

        var person = people.Add($"Listed {suffix}", Now);
        people.AddAlias(person.Id, raceroom, "4242", Now);
        people.AddAlias(person.Id, raceroom, "17", Now);
        people.AddAlias(person.Id, $"iracing-{suffix}", "881109", Now);
        await db.SaveChangesAsync();

        var claimed = await people.ListAliasesAsync(raceroom);

        claimed.Select(a => a.SimDriverId).ShouldBe(["17", "4242"]);
    }

    /// <summary>
    /// Ids are identifiers, never quantities. <c>"07"</c> and <c>"7"</c> are different strings and
    /// must stay two claims — parsing them would merge two accounts on a formatting difference.
    /// </summary>
    [Fact]
    public async Task Sim_driver_ids_are_compared_as_text_not_as_numbers()
    {
        SkipWithoutPostgres();

        await using var db = fixture.CreateContext();
        var people = new PersonRepository(db);
        var sim = $"raceroom-{Unique()}";

        var person = people.Add($"Leading zero {Unique()}", Now);
        people.AddAlias(person.Id, sim, "7", Now);
        people.AddAlias(person.Id, sim, "07", Now);

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
        (await people.ListAliasesAsync(sim)).Count.ShouldBe(2);
    }
}

using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Repositories;
using RaceIntelligence.Persistence.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

/// <summary>
/// Verifies <see cref="DriverRepository"/> keeps one person on one row.
/// </summary>
/// <remarks>
/// One database holds one simulator's sessions from several people, and telling their driving apart
/// is the entire point of the analysis layer — so a driver silently splitting into two rows does not surface as an
/// error, it surfaces as a model trained on half of someone's laps. Every test here is about a way
/// that split can happen: a rename, a session recorded before the sim id was ever captured, or a
/// session from a source that reported no id at all.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DriverResolutionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Resolving_the_same_sim_driver_id_twice_yields_one_row()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var repo = new DriverRepository(db);
        var simDriverId = UniqueSimDriverId();

        var first = await repo.ResolveOrCreateAsync(simDriverId, "Mark");
        var second = await repo.ResolveOrCreateAsync(simDriverId, "Mark");

        first!.Id.ShouldBe(second!.Id);
        (await db.Drivers.CountAsync(d => d.SimDriverId == simDriverId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_renamed_driver_keeps_one_row_and_takes_the_new_name()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var repo = new DriverRepository(db);
        var simDriverId = UniqueSimDriverId();

        var before = await repo.ResolveOrCreateAsync(simDriverId, "Old Name");
        var after = await repo.ResolveOrCreateAsync(simDriverId, "New Name");

        after!.Id.ShouldBe(before!.Id, "the sim driver id is the identity; the name is only a label");
        after.DisplayName.ShouldBe("New Name");
        (await db.Drivers.CountAsync(d => d.SimDriverId == simDriverId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_driver_first_seen_without_a_sim_id_is_adopted_rather_than_forked()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        // This is the state every driver row is left in by the migration that introduced
        // sim_driver_id — and the state an offline session produces, since RaceRoom reports no
        // usable account id then. If the sim-id path could not see these rows, the person's first
        // online session would start a second row and split their history permanently.
        await using var db = fixture.CreateContext();
        var repo = new DriverRepository(db);
        var simDriverId = UniqueSimDriverId();
        var name = UniqueName();

        var nameOnly = await repo.ResolveOrCreateAsync(simDriverId: null, name);
        nameOnly!.SimDriverId.ShouldBeNull();

        var withId = await repo.ResolveOrCreateAsync(simDriverId, name);

        withId!.Id.ShouldBe(nameOnly.Id, "the existing name-only row must be adopted, not duplicated");
        withId.SimDriverId.ShouldBe(simDriverId);
        (await db.Drivers.CountAsync(d => d.SimDriverId == simDriverId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_session_reporting_no_sim_id_attaches_to_the_driver_already_known_by_id()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        // The reverse crossing: an online session established the id, then an offline one reports
        // only the name. Growing a name-only twin beside the real row splits the same person again.
        await using var db = fixture.CreateContext();
        var repo = new DriverRepository(db);
        var name = UniqueName();

        var simDriverId = UniqueSimDriverId();

        var withId = await repo.ResolveOrCreateAsync(simDriverId, name);
        var nameOnly = await repo.ResolveOrCreateAsync(simDriverId: null, name);

        nameOnly!.Id.ShouldBe(withId!.Id);
        (await db.Drivers.CountAsync(d => d.SimDriverId == simDriverId)).ShouldBe(1);
    }

    [Fact]
    public async Task An_ambiguous_name_is_not_guessed_at()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        // Two real people sharing a display name, each with their own sim id. A nameless session
        // could belong to either, so attaching it to one of them would silently misattribute laps.
        // Keeping it separate is the honest outcome.
        await using var db = fixture.CreateContext();
        var repo = new DriverRepository(db);
        var name = UniqueName();

        var one = await repo.ResolveOrCreateAsync(UniqueSimDriverId(), name);
        var two = await repo.ResolveOrCreateAsync(UniqueSimDriverId(), name);
        one!.Id.ShouldNotBe(two!.Id, "different sim driver ids are different people");

        var ambiguous = await repo.ResolveOrCreateAsync(simDriverId: null, name);

        ambiguous!.Id.ShouldNotBe(one.Id);
        ambiguous.Id.ShouldNotBe(two.Id);
        ambiguous.SimDriverId.ShouldBeNull();
        (await db.Drivers.CountAsync(d => d.DisplayName == name)).ShouldBe(3);
    }

    [Fact]
    public async Task The_same_sim_driver_id_is_one_driver_because_the_database_is_one_simulator()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        // This replaces a test that asserted the opposite: that one sim driver id in two games was
        // two drivers, scoped by game_id. Sim driver ids do still share a numeric namespace across
        // simulators — that fact has not changed — but there is no longer a second simulator inside
        // this database for one to collide with, which is exactly what ADR 0001 traded away.
        //
        // Recognising one human across simulators is a different question with a different answer,
        // and deliberately not this row's job: it belongs to the identity registry in ADR 0002,
        // which is asserted by a person rather than derived from an id.
        await using var db = fixture.CreateContext();
        var repo = new DriverRepository(db);
        var simDriverId = UniqueSimDriverId();

        var first = await repo.ResolveOrCreateAsync(simDriverId, "Mark");
        var second = await repo.ResolveOrCreateAsync(simDriverId, "Mark");

        second!.Id.ShouldBe(first!.Id);
        (await db.Drivers.CountAsync(d => d.SimDriverId == simDriverId)).ShouldBe(1);
    }

    [Fact]
    public async Task Resolving_with_neither_an_id_nor_a_name_yields_null()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var repo = new DriverRepository(db);

        (await repo.ResolveOrCreateAsync(simDriverId: null, displayName: null)).ShouldBeNull();
        (await repo.ResolveOrCreateAsync(simDriverId: "   ", displayName: "  ")).ShouldBeNull(
            "whitespace is not an identity any more than null is");

        (await db.Drivers.CountAsync(d => d.DisplayName == "  ")).ShouldBe(0);
    }

    private static string UniqueSimDriverId() => $"sim-{Guid.NewGuid():N}";

    private static string UniqueName() => $"Driver {Guid.NewGuid():N}";
}

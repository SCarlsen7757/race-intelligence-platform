using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Repositories;
using RaceIntelligence.Persistence.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Persistence.Tests;

/// <summary>Verifies the resolve-or-create repositories are idempotent, per the platform's "reference data is never duplicated" requirement.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ResolveOrCreateTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Resolving_the_same_identity_twice_yields_one_version_row()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var identity = SampleFactory.UniqueGameVersion();
        var repo = new GameVersionRepository(db);

        var version1 = await repo.ResolveOrCreateAsync(identity);
        var version2 = await repo.ResolveOrCreateAsync(identity);

        version1.Id.ShouldBe(version2.Id);
        (await db.GameVersions.CountAsync(v => v.ConnectorVersion == identity.ConnectorVersion)).ShouldBe(1);
    }

    [Fact]
    public async Task Changing_only_the_connector_version_records_a_second_version_row()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var identity = SampleFactory.UniqueGameVersion(SampleFactory.Unique("connector"));
        var repo = new GameVersionRepository(db);

        var version1 = await repo.ResolveOrCreateAsync(identity);

        var updatedIdentity = identity with { ConnectorVersion = identity.ConnectorVersion + "-next" };
        var version2 = await repo.ResolveOrCreateAsync(updatedIdentity);

        version1.Id.ShouldNotBe(version2.Id, "a new connector version must be recorded as a new game_versions row");

        (await db.GameVersions.CountAsync(v => v.ConnectorVersion == identity.ConnectorVersion)).ShouldBe(1);
        (await db.GameVersions.CountAsync(v => v.ConnectorVersion == updatedIdentity.ConnectorVersion)).ShouldBe(1);
    }

    [Fact]
    public async Task Track_and_layout_resolution_is_idempotent()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var trackRepo = new TrackRepository(db);
        // Unique because tracks are no longer scoped by game, so two tests both resolving "Suzuka"
        // would legitimately share one row — which is correct behaviour and useless for counting.
        var trackName = SampleFactory.Unique("Suzuka");

        var (track1, layout1) = await trackRepo.ResolveOrCreateAsync(trackName, "Grand Prix", 5807);
        var (track2, layout2) = await trackRepo.ResolveOrCreateAsync(trackName, "Grand Prix", 5807);

        track1.Id.ShouldBe(track2.Id);
        layout1.Id.ShouldBe(layout2.Id);
        (await db.Tracks.CountAsync(t => t.Name == trackName)).ShouldBe(1);
        (await db.TrackLayouts.CountAsync(l => l.TrackId == track1.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Car_resolution_is_idempotent_and_reuses_manufacturer_and_class()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var carRepo = new CarRepository(db);
        var simCarId = SampleFactory.Unique("sim-car");

        var car1 = await carRepo.ResolveOrCreateCarAsync(simCarId, "GT3 Car", "Acme Motors", "GT3");
        var car2 = await carRepo.ResolveOrCreateCarAsync(simCarId, "GT3 Car", "Acme Motors", "GT3");

        car1.ShouldNotBeNull();
        car2.ShouldNotBeNull();
        car1.Id.ShouldBe(car2.Id);
        (await db.Cars.CountAsync(c => c.SimCarId == simCarId)).ShouldBe(1);
        (await db.Manufacturers.CountAsync(m => m.Name == "Acme Motors")).ShouldBe(1);
        (await db.CarClasses.CountAsync(c => c.Name == "GT3")).ShouldBe(1);
    }

    [Fact]
    public async Task Car_identity_is_the_sim_id_and_the_display_name_is_stored_separately()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();

        // The id and the name must differ, or a call that passes the name for both looks correct.
        var simCarId = SampleFactory.Unique("sim-car");
        var car = await new CarRepository(db).ResolveOrCreateCarAsync(simCarId, "Audi R8 LMS GT3");

        car.ShouldNotBeNull();
        car.SimCarId.ShouldBe(simCarId, "sim_car_id must carry the sim's own identifier, never the display name");
        car.Name.ShouldBe("Audi R8 LMS GT3");
    }

    [Fact]
    public async Task Renaming_a_car_updates_the_one_row_rather_than_forking_a_second()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var carRepo = new CarRepository(db);
        var simCarId = SampleFactory.Unique("sim-car");

        var before = await carRepo.ResolveOrCreateCarAsync(simCarId, "Old Car Name");
        var after = await carRepo.ResolveOrCreateCarAsync(simCarId, "New Car Name");

        before.ShouldNotBeNull();
        after.ShouldNotBeNull();
        after.Id.ShouldBe(before.Id, "the sim id is the identity, so a renamed car must not become a second row");
        after.Name.ShouldBe("New Car Name");
        (await db.Cars.CountAsync(c => c.SimCarId == simCarId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_car_with_a_sim_id_but_no_name_falls_back_to_the_id_as_its_label()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var carRepo = new CarRepository(db);
        var simCarId = SampleFactory.Unique("sim-car");

        var unnamed = await carRepo.ResolveOrCreateCarAsync(simCarId, null);
        unnamed.ShouldNotBeNull();
        unnamed.Name.ShouldBe(simCarId);

        // A later session that does report the name relabels the same row, exactly as for drivers.
        var named = await carRepo.ResolveOrCreateCarAsync(simCarId, "Discovered Name");
        named.ShouldNotBeNull();
        named.Id.ShouldBe(unnamed.Id);
        named.Name.ShouldBe("Discovered Name");
    }

    [Fact]
    public async Task A_car_with_neither_a_sim_id_nor_a_name_resolves_to_nothing()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();

        (await new CarRepository(db).ResolveOrCreateCarAsync(null, "  ")).ShouldBeNull();
        (await db.Cars.CountAsync(c => c.Name == "  ")).ShouldBe(0);
    }
}

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
    public async Task Resolving_the_same_identity_twice_yields_one_game_row_and_one_version_row()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var identity = SampleFactory.UniqueGameVersion();
        var repo = new GameRepository(db);

        var (game1, version1) = await repo.ResolveOrCreateAsync(identity);
        var (game2, version2) = await repo.ResolveOrCreateAsync(identity);

        game1.Id.ShouldBe(game2.Id);
        version1.Id.ShouldBe(version2.Id);

        (await db.Games.CountAsync(g => g.Key == identity.Game.Key)).ShouldBe(1);
        (await db.GameVersions.CountAsync(v => v.GameId == game1.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Changing_only_connector_version_creates_a_second_version_but_reuses_the_game()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var identity = SampleFactory.UniqueGameVersion(connectorVersion: "1.0.0");
        var repo = new GameRepository(db);

        var (game1, version1) = await repo.ResolveOrCreateAsync(identity);

        var updatedIdentity = identity with { ConnectorVersion = "1.0.1" };
        var (game2, version2) = await repo.ResolveOrCreateAsync(updatedIdentity);

        game1.Id.ShouldBe(game2.Id, "the game itself did not change, only the connector version");
        version1.Id.ShouldNotBe(version2.Id, "a new connector version must be recorded as a new game_versions row");

        (await db.Games.CountAsync(g => g.Key == identity.Game.Key)).ShouldBe(1);
        (await db.GameVersions.CountAsync(v => v.GameId == game1.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Track_and_layout_resolution_is_idempotent()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Postgres container unavailable.");
        }

        await using var db = fixture.CreateContext();
        var (game, _) = await new GameRepository(db).ResolveOrCreateAsync(SampleFactory.UniqueGameVersion());
        var trackRepo = new TrackRepository(db);

        var (track1, layout1) = await trackRepo.ResolveOrCreateAsync(game.Id, "Suzuka", "Grand Prix", 5807);
        var (track2, layout2) = await trackRepo.ResolveOrCreateAsync(game.Id, "Suzuka", "Grand Prix", 5807);

        track1.Id.ShouldBe(track2.Id);
        layout1.Id.ShouldBe(layout2.Id);
        (await db.Tracks.CountAsync(t => t.GameId == game.Id)).ShouldBe(1);
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
        var (game, _) = await new GameRepository(db).ResolveOrCreateAsync(SampleFactory.UniqueGameVersion());
        var carRepo = new CarRepository(db);

        var car1 = await carRepo.ResolveOrCreateCarAsync(game.Id, "sim-car-1", "GT3 Car", "Acme Motors", "GT3");
        var car2 = await carRepo.ResolveOrCreateCarAsync(game.Id, "sim-car-1", "GT3 Car", "Acme Motors", "GT3");

        car1.Id.ShouldBe(car2.Id);
        (await db.Cars.CountAsync(c => c.GameId == game.Id)).ShouldBe(1);
        (await db.Manufacturers.CountAsync(m => m.Name == "Acme Motors")).ShouldBe(1);
        (await db.CarClasses.CountAsync(c => c.Name == "GT3")).ShouldBe(1);
    }
}

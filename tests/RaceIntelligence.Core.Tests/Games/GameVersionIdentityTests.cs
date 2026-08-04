using RaceIntelligence.Core.Games;
using Shouldly;

namespace RaceIntelligence.Core.Tests.Games;

public class GameVersionIdentityTests
{
    private static GameVersionIdentity Create(string connectorVersion = "1.0.0", int apiMinor = 0) => new()
    {
        Game = WellKnownGames.RaceRoom,
        GameVersion = "1.2.3.4",
        ApiVersionMajor = 2,
        ApiVersionMinor = apiMinor,
        ConnectorVersion = connectorVersion,
    };

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = Create();
        var b = Create();

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
    }

    [Fact]
    public void Equality_DifferentConnectorVersion_AreNotEqual()
    {
        var a = Create(connectorVersion: "1.0.0");
        var b = Create(connectorVersion: "1.0.1");

        a.ShouldNotBe(b);
        (a != b).ShouldBeTrue();
    }

    [Fact]
    public void Equality_DifferentApiVersionMinor_AreNotEqual()
    {
        var a = Create(apiMinor: 0);
        var b = Create(apiMinor: 1);

        a.ShouldNotBe(b);
        (a != b).ShouldBeTrue();
    }

    [Fact]
    public void Equality_DifferentGameVersionString_AreNotEqual()
    {
        var a = Create() with { GameVersion = "1.0.0.0" };
        var b = Create() with { GameVersion = "1.0.0.1" };

        a.ShouldNotBe(b);
    }
}

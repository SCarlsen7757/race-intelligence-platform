using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Collector.Tests.Support;

/// <summary>Builds minimal-but-valid <see cref="SessionInfo"/> instances for tests.</summary>
internal static class SessionInfoFactory
{
    public static SessionInfo Create(Guid? sessionId = null, DateTimeOffset? startedAtUtc = null) => new()
    {
        SessionId = sessionId ?? Guid.NewGuid(),
        GameVersion = new GameVersionIdentity
        {
            Game = WellKnownGames.RaceRoom,
            GameVersion = "1.2.3.4",
            ApiVersionMajor = 3,
            ApiVersionMinor = 5,
            ConnectorVersion = "0.1.0",
        },
        Capabilities = SimCapabilities.TyreWear | SimCapabilities.TyrePressure,
        TrackName = "Test Track",
        LayoutName = "Test Layout",
        LayoutLengthMeters = 4000f,
        SessionType = SessionType.Practice,
        StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow,
        PlayerName = "Test Driver",
        CarName = "Test Car",
    };
}

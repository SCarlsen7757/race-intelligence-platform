using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Collector.TestSupport;

/// <summary>Builds minimal-but-valid <see cref="SessionInfo"/> instances for tests.</summary>
public static class SessionInfoFactory
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
        // Distinct non-default values on purpose. FuelUsageRate and TyreWearRate are both int? and
        // are passed positionally by CollectorRequestMapper, so equal values here would let the two
        // be swapped without any test noticing — see CollectorRequestMapperTests.
        SimDriverId = "sim-driver-4711",
        CarName = "Test Car",
        FuelUsageRate = 2,
        TyreWearRate = 3,
    };
}

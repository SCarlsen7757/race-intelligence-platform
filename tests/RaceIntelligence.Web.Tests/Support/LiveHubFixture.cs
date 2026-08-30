using RaceIntelligence.Collector.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Web.Live;

namespace RaceIntelligence.Web.Tests.Support;

/// <summary>
/// Builds the hub's object graph the way <c>Program.cs</c> does, on a controllable clock.
/// </summary>
/// <remarks>
/// Assembled by hand rather than through a host: the graph is four singletons, and building it
/// directly keeps the fake clock in the test's hands. Room expiry is measured in tens of seconds,
/// which is not something to wait for in a test suite.
/// </remarks>
public sealed class LiveHubFixture
{
    public LiveHubFixture(TimeSpan? roomExpiry = null)
    {
        Options = Microsoft.Extensions.Options.Options.Create(new LiveHubOptions
        {
            ApiKey = "test-key",
            AllowedOrigins = ["http://dashboard.test"],
            RoomExpiry = roomExpiry ?? TimeSpan.FromSeconds(30),
        });

        Time = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-16T12:00:00Z", null));
        Viewers = new LiveViewerRegistry();
        Rooms = new LiveRoomRegistry(Viewers, Options, Time, NullLogger<LiveRoomRegistry>.Instance);
    }

    public IOptions<LiveHubOptions> Options { get; }

    public FakeTimeProvider Time { get; }

    public LiveViewerRegistry Viewers { get; }

    public LiveRoomRegistry Rooms { get; }

    public PublisherSession CreatePublisherSession() =>
        new(Rooms, Time, NullLogger<PublisherSession>.Instance);

    public ViewerSession CreateViewerSession() =>
        new(Rooms, Viewers, Time, NullLogger<ViewerSession>.Instance);

    /// <summary>Registers a viewer directly, for tests about fan-out rather than about the socket.</summary>
    public LiveViewer AddViewer(string? roomId = null, string? focusDriverKey = null)
    {
        var viewer = new LiveViewer();
        Viewers.Add(viewer);

        if (roomId is not null)
        {
            viewer.WatchRoom(roomId);
        }

        if (focusDriverKey is not null)
        {
            viewer.Focus(focusDriverKey);
        }

        return viewer;
    }

    /// <summary>Announces a session for a publisher and returns the room it landed in.</summary>
    public LiveRoom AnnounceRoom(
        LivePublisherIdentity identity,
        string track = "Spa",
        string layout = "Grand Prix",
        int sessionType = 3,
        int sessionIteration = 1,
        string? localSimDriverId = null,
        int? localSlotId = null,
        string? playerName = "Mark")
    {
        Rooms.Announce(identity, LiveDtoFactory.SessionFrame(
            track: track,
            layout: layout,
            sessionType: sessionType,
            sessionIteration: sessionIteration,
            localSimDriverId: localSimDriverId,
            localSlotId: localSlotId,
            playerName: playerName));

        var summary = Rooms.BuildRoomList().Rooms
            .First(room => string.Equals(room.TrackName, track, StringComparison.Ordinal)
                && room.SessionType == sessionType);

        return Rooms.FindByRoomId(summary.RoomId)!;
    }
}

/// <summary>Builds the wire and canonical shapes the hub tests exercise.</summary>
public static class LiveDtoFactory
{
    public static LivePublisherIdentity Identity(
        Guid? clientId = null,
        string clientName = "Gaming PC",
        DateTimeOffset? connectedAtUtc = null) =>
        new(
            clientId ?? Guid.NewGuid(),
            clientName,
            "1.0.0",
            connectedAtUtc ?? DateTimeOffset.Parse("2026-08-16T12:00:00Z", null));

    public static LiveHello Hello(Guid? clientId = null, int? schemaVersion = null) =>
        new(
            schemaVersion ?? RaceIntelligence.Live.Contracts.LiveSchemaVersion.Current,
            clientId ?? Guid.NewGuid(),
            "Gaming PC",
            "1.0.0",
            "raceroom",
            Capabilities: 0);

    public static LiveSessionFrame SessionFrame(
        Guid? sessionId = null,
        string track = "Spa",
        string layout = "Grand Prix",
        int sessionType = 3,
        int sessionIteration = 1,
        string? localSimDriverId = null,
        string rosterFingerprint = "fingerprint",
        int rosterSize = 2,
        int? localSlotId = null,
        string? playerName = "Mark") =>
        new(
            sessionId ?? Guid.NewGuid(),
            "raceroom",
            track,
            layout,
            LayoutLengthMeters: 7004f,
            sessionType,
            sessionIteration,
            DateTimeOffset.Parse("2026-08-16T12:00:00Z", null),
            playerName,
            localSimDriverId,
            rosterFingerprint,
            rosterSize,
            localSlotId);

    /// <summary>A standings frame whose drivers are numbered 1..<paramref name="driverCount"/>.</summary>
    /// <param name="useSlotIdsInsteadOfDriverIds">
    /// Builds the roster the way an offline session arrives — every car identified by slot alone,
    /// because the simulator issues no account ids outside an authenticated online session. The
    /// rows then key on <c>slot:</c> rather than <c>id:</c>, which is what the local car has to
    /// match to get a focus stream.
    /// </param>
    public static LiveStandingsFrame StandingsFrame(
        Guid? sessionId = null,
        int driverCount = 3,
        string? localSimDriverId = null,
        DateTimeOffset? capturedAtUtc = null,
        bool useSlotIdsInsteadOfDriverIds = false) =>
        new(
            sessionId ?? Guid.NewGuid(),
            capturedAtUtc ?? DateTimeOffset.Parse("2026-08-16T12:00:00Z", null),
            SimulationTime: 42.0,
            localSimDriverId,
            [.. Enumerable.Range(1, driverCount).Select(i => useSlotIdsInsteadOfDriverIds
                ? Driver(slotId: i, displayName: $"Driver {i}", position: i)
                : Driver(simDriverId: i.ToString(), position: i))]);

    public static LiveDriverDto Driver(
        string? simDriverId = null,
        int? slotId = null,
        string displayName = "Driver",
        int? position = null,
        int? completedLaps = null,
        TimeSpan? previousLapTime = null,
        IReadOnlyList<TimeSpan?>? previousSectorTimes = null,
        bool? currentLapValid = null) => new()
        {
            SimDriverId = simDriverId,
            SlotId = slotId,
            DisplayName = displayName,
            Position = position,
            CompletedLaps = completedLaps,
            PreviousLapTime = previousLapTime,
            PreviousSectorTimes = previousSectorTimes,
            CurrentLapValid = currentLapValid,
            PitLaneState = (int)Core.Sessions.PitLaneState.Unavailable,
            PitStopStatus = (int)Core.Sessions.PitStopStatus.Unavailable,
            FinishStatus = (int)DriverFinishStatus.Unavailable,
        };

    /// <summary>A standings frame carrying exactly the drivers given, for lap-history tests.</summary>
    public static LiveStandingsFrame StandingsFrameOf(Guid sessionId, params LiveDriverDto[] drivers) =>
        new(
            sessionId,
            DateTimeOffset.Parse("2026-08-16T12:00:00Z", null),
            SimulationTime: 42.0,
            LocalSimDriverId: null,
            drivers);

    /// <summary>A standings frame whose only interesting content is the session's pit window.</summary>
    public static LiveStandingsFrame StandingsFrameWithWindow(
        Guid sessionId,
        PitWindowStatus status,
        int? start = 12,
        int? end = 20,
        PitWindowUnit unit = PitWindowUnit.Laps) =>
        StandingsFrameOf(sessionId, Driver(simDriverId: "77", position: 1)) with
        {
            PitWindow = new LivePitWindowDto((int)status, start, end, (int)unit),
        };

    public static LiveSelfFrame SelfFrame(
        Guid? sessionId = null,
        string? simDriverId = null,
        long sequenceNumber = 1,
        float? clutch = null,
        int? absSetting = null,
        bool? absActive = null,
        int? tractionControlSetting = null,
        bool? tractionControlActive = null,
        float? brakeBias = null) =>
        new(
            sessionId ?? Guid.NewGuid(),
            simDriverId,
            sequenceNumber,
            DateTimeOffset.Parse("2026-08-16T12:00:00Z", null),
            SimulationTime: 42.0,
            LapNumber: 2,
            Sector: 1,
            TrackPositionFraction: 0.25f,
            Speed: 55f,
            Throttle: 1f,
            Brake: 0f,
            Steering: 0.1f,
            Gear: 4,
            EngineRpm: 7200f,
            FuelLeft: 40f,
            new LiveWheelValues(3.1f, 3.2f, 1.4f, 1.5f),
            clutch,
            absSetting,
            absActive,
            tractionControlSetting,
            tractionControlActive,
            brakeBias);

    /// <summary>The tyre channels, on the slower frame they travel on.</summary>
    public static LiveStintFrame StintFrame(
        Guid? sessionId = null,
        string? simDriverId = null) =>
        new(
            sessionId ?? Guid.NewGuid(),
            simDriverId,
            DateTimeOffset.Parse("2026-08-16T12:00:00Z", null),
            new LiveWheelValues(180f, 181f, 175f, 176f),
            new LiveWheelValues(0.1f, 0.11f, 0.09f, 0.12f),
            new LiveTyreTemperatures(
                new LiveTreadTemperatures(84f, 85f, 86f),
                new LiveTreadTemperatures(85f, 86f, 87f),
                new LiveTreadTemperatures(81f, 82f, 83f),
                new LiveTreadTemperatures(82f, 83f, 84f)));

    /// <summary>
    /// A slow-channel frame. <paramref name="damageEngine"/> is what a test identifies it by, and
    /// leaving it null is the "the simulator did not report this" case that used to be a <c>-1</c>
    /// inside a JSON string nothing typed.
    /// </summary>
    public static LiveSlowFrame SlowFrame(
        Guid? sessionId = null,
        string? simDriverId = null,
        float? damageEngine = 0.5f) =>
        new(
            sessionId ?? Guid.NewGuid(),
            simDriverId,
            DateTimeOffset.Parse("2026-08-16T12:00:00Z", null),
            TelemetrySampleFactory.Create(sessionId ?? Guid.NewGuid()) with { DamageEngine = damageEngine },
            OperatingWindowFactory.Create());

    /// <summary>A canonical standing, for tests of the projection rather than of the wire.</summary>
    public static DriverStanding Standing(
        string? simDriverId = null,
        int? slotId = null,
        string displayName = "Driver",
        int? position = null,
        TimeSpan? bestLap = null) => new()
        {
            SimDriverId = simDriverId,
            SlotId = slotId,
            DisplayName = displayName,
            Position = position,
            BestLapTime = bestLap,
        };

    public static SessionStandings Standings(params DriverStanding[] drivers) => new()
    {
        SessionId = Guid.NewGuid(),
        CapturedAtUtc = DateTimeOffset.Parse("2026-08-16T12:00:00Z", null),
        Drivers = drivers,
    };
}

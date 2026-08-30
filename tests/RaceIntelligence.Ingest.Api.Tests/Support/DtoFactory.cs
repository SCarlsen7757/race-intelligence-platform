using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Telemetry;

namespace RaceIntelligence.Ingest.Api.Tests.Support;

/// <summary>Builders for wire DTOs, kept small and explicit for test readability. Mirrors <c>RaceIntelligence.Persistence.RaceRoom.Tests.Support.SampleFactory</c>.</summary>
internal static class DtoFactory
{
    /// <summary>
    /// Builds a <see cref="GameVersionDto"/> that this ingest API will accept, unique enough that
    /// tests never share a <c>game_versions</c> row.
    /// </summary>
    /// <remarks>
    /// The game key has to be the real one now. It used to be a per-test invention, because the key
    /// only had to be unique within a <c>games</c> table that no longer exists; the ingest API now
    /// checks it against the simulator it is configured for and refuses anything else (ADR 0001).
    /// Uniqueness therefore moves to the connector version, which is part of what actually keys a
    /// version row.
    /// </remarks>
    public static GameVersionDto UniqueGameVersion(string? connectorVersion = null)
    {
        var version = connectorVersion ?? $"test-{Guid.NewGuid():N}";
        return new GameVersionDto(GameKey, "RaceRoom Racing Experience", "1.2.3", 1, 0, version);
    }

    /// <summary>
    /// The simulator this ingest API is configured for, matching <c>Ingest__GameKey</c> in the
    /// AppHost. A session claiming anything else is refused — see <c>SessionEndpoints</c>.
    /// </summary>
    public const string GameKey = "raceroom";

    /// <summary>Builds a well-formed <see cref="SessionCreateRequest"/>, optionally for a specific session id.</summary>
    public static SessionCreateRequest SessionCreateRequest(Guid? sessionId = null, int? schemaVersion = null) => new(
        SchemaVersion: schemaVersion ?? RaceIntelligence.Ingest.Contracts.SchemaVersion.Current,
        SessionId: sessionId ?? Guid.CreateVersion7(),
        GameVersion: UniqueGameVersion(),
        Capabilities: 0,
        TrackName: "Suzuka",
        LayoutName: "Grand Prix",
        LayoutLengthMeters: 5807f,
        SessionType: 3, // Race
        StartedAtUtc: DateTimeOffset.UtcNow,
        PlayerName: "Test Driver",
        CarName: "Test GT3 Car",
        CarClassName: "GT3",
        ManufacturerName: "Acme Motors",
        ExtrasJson: null);

    /// <summary>A minimal, complete sample for <paramref name="sessionId"/> at <paramref name="sequenceNumber"/>.</summary>
    /// <remarks>
    /// Sets the channels these tests read and leaves the rest at their defaults, which for a nullable
    /// channel is <see langword="null"/> — the same thing it means everywhere else. Filling all
    /// hundred and seventy-five would assert a shape rather than supply a sample.
    /// </remarks>
    public static RaceRoomTelemetrySample TelemetrySample(Guid sessionId, long sequenceNumber, DateTimeOffset? timestamp = null) => new()
    {
        SessionId = sessionId,
        SequenceNumber = sequenceNumber,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow.AddMilliseconds(sequenceNumber),
        SimulationTime = sequenceNumber * 0.1,
        Speed = 45.5f,
        Throttle = 0.8f,
        Brake = 0f,
        Clutch = 0.25f,
        Steering = 0.1f,
        Gear = 4,
        EngineRpm = 6500f,
        FuelLeft = 40.2f,
        LapNumber = 1,
        Sector = 1,
        Position = 3,
        TrackPositionFraction = 0.42f,
        WheelSpeedFl = 45.1f,
        WheelSpeedFr = 45.2f,
        WheelSpeedRl = 44.9f,
        WheelSpeedRr = 45.0f,
        SuspensionTravelFl = 0.05f,
        SuspensionTravelFr = 0.05f,
        SuspensionTravelRl = 0.06f,
        SuspensionTravelRr = 0.06f,
        TyrePressureFl = 180f,
        TyrePressureFr = 180f,
        TyrePressureRl = 175f,
        TyrePressureRr = 175f,
        TyreWearFl = 0.1f,
        TyreWearFr = 0.1f,
        TyreWearRl = 0.12f,
        TyreWearRr = 0.12f,
        TyreTempFlInner = 85f,
        TyreTempFlMiddle = 90f,
        TyreTempFlOuter = 88f,
        TyreTempFrInner = 85f,
        TyreTempFrMiddle = 90f,
        TyreTempFrOuter = 88f,
        TyreTempRlInner = 85f,
        TyreTempRlMiddle = 90f,
        TyreTempRlOuter = 88f,
        TyreTempRrInner = 85f,
        TyreTempRrMiddle = 90f,
        TyreTempRrOuter = 88f,
    };

    /// <summary>Builds <paramref name="count"/> sequential telemetry samples starting at <paramref name="startSequence"/>.</summary>
    public static IReadOnlyList<RaceRoomTelemetrySample> TelemetryBatch(Guid sessionId, int count, long startSequence = 0, DateTimeOffset? anchor = null)
    {
        var start = anchor ?? DateTimeOffset.UtcNow;
        var samples = new List<RaceRoomTelemetrySample>(count);
        for (var i = 0; i < count; i++)
        {
            var sequenceNumber = startSequence + i;
            samples.Add(TelemetrySample(sessionId, sequenceNumber, start.AddMilliseconds(sequenceNumber * 20)));
        }

        return samples;
    }

    /// <summary>The tyre and brake bands, one per corner, as they ride on a telemetry batch.</summary>
    public static IReadOnlyList<OperatingWindow> OperatingWindows() =>
    [
        new(Corner.FrontLeft, Compound: 2, 90f, 70f, 110f, 410f, 200f, 600f),
        new(Corner.FrontRight, Compound: 2, 90f, 70f, 110f, 410f, 200f, 600f),
        new(Corner.RearLeft, Compound: 4, 92f, 72f, 112f, 412f, 202f, 602f),
        new(Corner.RearRight, Compound: 4, 92f, 72f, 112f, 412f, 202f, 602f),
    ];
}

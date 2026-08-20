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

    /// <summary>A minimal, complete <see cref="TelemetrySampleDto"/> for <paramref name="sessionId"/> at <paramref name="sequenceNumber"/>.</summary>
    public static TelemetrySampleDto TelemetrySample(Guid sessionId, long sequenceNumber, DateTimeOffset? timestamp = null) => new()
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
        WheelSpeedFrontLeft = 45.1f,
        WheelSpeedFrontRight = 45.2f,
        WheelSpeedRearLeft = 44.9f,
        WheelSpeedRearRight = 45.0f,
        SuspensionTravelFrontLeft = 0.05f,
        SuspensionTravelFrontRight = 0.05f,
        SuspensionTravelRearLeft = 0.06f,
        SuspensionTravelRearRight = 0.06f,
        TyrePressureFrontLeft = 180f,
        TyrePressureFrontRight = 180f,
        TyrePressureRearLeft = 175f,
        TyrePressureRearRight = 175f,
        TyreWearFrontLeft = 0.1f,
        TyreWearFrontRight = 0.1f,
        TyreWearRearLeft = 0.12f,
        TyreWearRearRight = 0.12f,
        TyreTemperatureFrontLeft = TyreTemperature(),
        TyreTemperatureFrontRight = TyreTemperature(),
        TyreTemperatureRearLeft = TyreTemperature(),
        TyreTemperatureRearRight = TyreTemperature(),
        TrackPositionFraction = 0.42f,
        Extras = "{}",
    };

    /// <summary>Builds <paramref name="count"/> sequential telemetry sample DTOs starting at <paramref name="startSequence"/>.</summary>
    public static IReadOnlyList<TelemetrySampleDto> TelemetryBatch(Guid sessionId, int count, long startSequence = 0, DateTimeOffset? anchor = null)
    {
        var start = anchor ?? DateTimeOffset.UtcNow;
        var samples = new List<TelemetrySampleDto>(count);
        for (var i = 0; i < count; i++)
        {
            var sequenceNumber = startSequence + i;
            samples.Add(TelemetrySample(sessionId, sequenceNumber, start.AddMilliseconds(sequenceNumber * 20)));
        }

        return samples;
    }

    private static TyreTemperatureDto TyreTemperature() => new() { Inner = 85, Middle = 90, Outer = 88, Optimal = 90, Cold = 70, Hot = 110 };
}

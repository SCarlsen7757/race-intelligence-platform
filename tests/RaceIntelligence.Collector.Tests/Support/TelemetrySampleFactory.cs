using System.Text.Json;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Tests.Support;

/// <summary>Builds minimal-but-valid <see cref="TelemetrySample"/> instances for tests.</summary>
internal static class TelemetrySampleFactory
{
    private const string EmptyExtras = "{}";

    public static TelemetrySample Create(Guid sessionId, long sequenceNumber = 0, DateTimeOffset? timestamp = null) => new()
    {
        SessionId = sessionId,
        SequenceNumber = sequenceNumber,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        SimulationTime = sequenceNumber,
        Speed = 45f,
        Throttle = 1f,
        Brake = 0f,
        Steering = 0f,
        Gear = 4,
        EngineRpm = 6500f,
        FuelLeft = 35f,
        LapNumber = 1,
        Sector = 1,
        Position = 1,
        WheelSpeed = new WheelData<float>(45f, 45f, 45f, 45f),
        SuspensionTravel = new WheelData<float>(0.03f, 0.03f, 0.03f, 0.03f),
        TyreTemperature = new WheelData<TyreTemperature>(default, default, default, default),
        TyrePressure = new WheelData<float?>(180f, 180f, 180f, 180f),
        TyreWear = new WheelData<float?>(0f, 0f, 0f, 0f),
        TrackPositionFraction = 0.1f,
        Extras = EmptyExtras,
    };
}

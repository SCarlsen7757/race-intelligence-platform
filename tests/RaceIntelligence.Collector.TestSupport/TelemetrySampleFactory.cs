using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Collector.TestSupport;

/// <summary>Builds minimal-but-valid <see cref="RaceRoomTelemetrySample"/> instances for tests.</summary>
/// <remarks>
/// Sets the handful of channels a test is likely to assert on and leaves the rest at their defaults,
/// which for a nullable channel is <see langword="null"/> — "the simulator did not report this", the
/// same thing it means everywhere else. A factory that filled all hundred and seventy-five would be
/// asserting a shape rather than supplying a sample.
/// </remarks>
public static class TelemetrySampleFactory
{
    public static RaceRoomTelemetrySample Create(Guid sessionId, long sequenceNumber = 0, DateTimeOffset? timestamp = null) => new()
    {
        SessionId = sessionId,
        SequenceNumber = sequenceNumber,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        SimulationTime = sequenceNumber,
        Speed = 45f,
        Throttle = 1f,
        Brake = 0f,
        Clutch = 0.5f,
        Steering = 0f,
        Gear = 4,
        EngineRpm = 6500f,
        FuelLeft = 35f,
        LapNumber = 1,
        Sector = 1,
        Position = 1,
        TrackPositionFraction = 0.1f,
        WheelSpeedFl = 45f,
        WheelSpeedFr = 45f,
        WheelSpeedRl = 45f,
        WheelSpeedRr = 45f,
        SuspensionTravelFl = 0.03f,
        SuspensionTravelFr = 0.03f,
        SuspensionTravelRl = 0.03f,
        SuspensionTravelRr = 0.03f,
        TyrePressureFl = 180f,
        TyrePressureFr = 180f,
        TyrePressureRl = 180f,
        TyrePressureRr = 180f,
        TyreWearFl = 0f,
        TyreWearFr = 0f,
        TyreWearRl = 0f,
        TyreWearRr = 0f,
    };
}

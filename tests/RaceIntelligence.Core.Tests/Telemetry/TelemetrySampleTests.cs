using System.Text.Json;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Core.Tests.Telemetry;

public class TelemetrySampleTests
{
    private static TelemetrySample CreateSample(Guid? sessionId = null) => new()
    {
        SessionId = sessionId ?? Guid.NewGuid(),
        SequenceNumber = 1,
        Timestamp = DateTimeOffset.UtcNow,
        SimulationTime = 12.5,
        Speed = 42f,
        Steering = 0f,
        Gear = 3,
        EngineRpm = 6500f,
        FuelLeft = 30f,
        LapNumber = 2,
        Sector = 1,
        WheelSpeed = new WheelData<float>(1, 2, 3, 4),
        SuspensionTravel = new WheelData<float>(0.1f, 0.1f, 0.1f, 0.1f),
        TyreTemperature = new WheelData<TyreTemperature>(
            new TyreTemperature(80, 82, 84, 90, 70, 110),
            new TyreTemperature(80, 82, 84, 90, 70, 110),
            new TyreTemperature(80, 82, 84, 90, 70, 110),
            new TyreTemperature(80, 82, 84, 90, 70, 110)),
        Extras = default,
    };

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var id = Guid.NewGuid();
        var a = CreateSample(id) with { Timestamp = DateTimeOffset.UnixEpoch };
        var b = CreateSample(id) with { Timestamp = DateTimeOffset.UnixEpoch };

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        var a = CreateSample();
        var b = a with { Speed = a.Speed + 1 };

        a.ShouldNotBe(b);
    }

    [Fact]
    public void NullableFields_DefaultToNull()
    {
        var sample = CreateSample();

        sample.Throttle.ShouldBeNull();
        sample.Brake.ShouldBeNull();
        sample.Position.ShouldBeNull();
        sample.TrackPositionFraction.ShouldBeNull();
        sample.TyrePressure.FrontLeft.ShouldBeNull();
        sample.TyrePressure.FrontRight.ShouldBeNull();
        sample.TyrePressure.RearLeft.ShouldBeNull();
        sample.TyrePressure.RearRight.ShouldBeNull();
        sample.TyreWear.FrontLeft.ShouldBeNull();
        sample.TyreWear.FrontRight.ShouldBeNull();
        sample.TyreWear.RearLeft.ShouldBeNull();
        sample.TyreWear.RearRight.ShouldBeNull();
    }

    [Fact]
    public void NotAvailableSentinel_MustBeTranslatedToNull_NotZero()
    {
        // Documents the contract: a connector translating a source's "not available" sentinel
        // (e.g. RaceRoom's -1.0) must produce null, never 0 -- 0 has a distinct real meaning
        // (empty fuel tank, zero wear).
        var sample = CreateSample() with
        {
            FuelLeft = 0f, // a real, valid "empty tank" reading
            TyreWear = new WheelData<float?>(null, null, null, null), // "not available" from the source
        };

        sample.FuelLeft.ShouldBe(0f);
        sample.TyreWear.FrontLeft.ShouldBeNull();
    }
}

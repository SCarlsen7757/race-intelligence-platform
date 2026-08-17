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
        Extras = "{}",
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
        sample.Clutch.ShouldBeNull();
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
    public void FieldsThatMayBeUnavailable_AreNullable_SoASentinelHasSomewhereToGo()
    {
        // Replaces a test that built a record with null tyre wear and then asserted the tyre wear
        // was null -- it exercised the `with` expression, not the sentinel translation its name
        // claimed. That translation belongs to (and is tested in) the connectors; see
        // R3ETelemetryMapperSentinelTests. What Core owns is the shape that makes it expressible:
        // a field a source may not report must be nullable, so a connector is never forced to pick
        // a real value like 0 to stand in for "unavailable".
        var type = typeof(TelemetrySample);

        foreach (string propertyName in new[] { nameof(TelemetrySample.Throttle), nameof(TelemetrySample.Brake), nameof(TelemetrySample.Clutch), nameof(TelemetrySample.Position), nameof(TelemetrySample.TrackPositionFraction) })
        {
            var propertyType = type.GetProperty(propertyName)!.PropertyType;
            Nullable.GetUnderlyingType(propertyType).ShouldNotBeNull($"{propertyName} must be nullable so 'not reported' is distinct from a real reading.");
        }

        // Per-wheel optional data is nullable per wheel, not per set: a sim can report pressure for
        // some wheels and not others.
        type.GetProperty(nameof(TelemetrySample.TyrePressure))!.PropertyType.ShouldBe(typeof(WheelData<float?>));
        type.GetProperty(nameof(TelemetrySample.TyreWear))!.PropertyType.ShouldBe(typeof(WheelData<float?>));

        // ...while a field every sim reliably reports stays non-nullable and required, so 0 there
        // is unambiguously a real reading (an empty tank), never a stand-in for "unknown".
        Nullable.GetUnderlyingType(type.GetProperty(nameof(TelemetrySample.FuelLeft))!.PropertyType).ShouldBeNull();
    }
}

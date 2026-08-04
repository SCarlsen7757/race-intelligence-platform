using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Verifies <see cref="R3ETelemetryMapper.ToSample"/>'s sentinel handling: RaceRoom's <c>-1</c>
/// (or <c>-1.0</c>) "not available" sentinel must become <see langword="null"/>, and must never be
/// coerced to <c>0</c> — a reading of "unavailable" is not the same as a reading of "zero", and
/// because raw telemetry is stored permanently, conflating the two would corrupt history forever.
/// Fields that are legitimately negative in normal operation (steering, gear) must pass straight
/// through, unmodified.
/// </summary>
public class R3ETelemetryMapperSentinelTests
{
    private static TelemetrySample MapSample(Action<R3ESharedRawBuilder> configure)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Sentinel Test Track", "Sentinel Test Layout");
        configure(builder);
        var raw = builder.Build();
        return R3ETelemetryMapper.ToSample(in raw, Guid.NewGuid(), sequenceNumber: 0, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Throttle_NegativeSentinel_MapsToNull()
    {
        var sample = MapSample(b => b.WithThrottle(-1f));
        sample.Throttle.ShouldBeNull();
    }

    [Fact]
    public void Throttle_Zero_MapsToZero_NotNull()
    {
        var sample = MapSample(b => b.WithThrottle(0f));
        sample.Throttle.ShouldBe(0f);
    }

    [Fact]
    public void Throttle_PositiveValue_PassesThrough()
    {
        var sample = MapSample(b => b.WithThrottle(0.73f));
        sample.Throttle.ShouldBe(0.73f);
    }

    [Fact]
    public void Brake_NegativeSentinel_MapsToNull()
    {
        var sample = MapSample(b => b.WithBrake(-1f));
        sample.Brake.ShouldBeNull();
    }

    [Fact]
    public void Brake_Zero_MapsToZero_NotNull()
    {
        var sample = MapSample(b => b.WithBrake(0f));
        sample.Brake.ShouldBe(0f);
    }

    [Fact]
    public void Brake_PositiveValue_PassesThrough()
    {
        var sample = MapSample(b => b.WithBrake(0.42f));
        sample.Brake.ShouldBe(0.42f);
    }

    [Theory]
    [InlineData(-1f, -1f, -1f, -1f, null, null, null, null)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f)]
    [InlineData(-1f, 120f, -1f, 130f, null, 120f, null, 130f)]
    public void TyrePressure_NegativeSentinelPerWheel_MapsToNull(
        float frontLeft, float frontRight, float rearLeft, float rearRight,
        float? expectedFrontLeft, float? expectedFrontRight, float? expectedRearLeft, float? expectedRearRight)
    {
        var sample = MapSample(b => b.WithTyrePressures(frontLeft, frontRight, rearLeft, rearRight));

        sample.TyrePressure.FrontLeft.ShouldBe(expectedFrontLeft);
        sample.TyrePressure.FrontRight.ShouldBe(expectedFrontRight);
        sample.TyrePressure.RearLeft.ShouldBe(expectedRearLeft);
        sample.TyrePressure.RearRight.ShouldBe(expectedRearRight);
    }

    [Theory]
    [InlineData(-1f, -1f, -1f, -1f, null, null, null, null)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f)]
    [InlineData(-1f, 0.15f, -1f, 0.22f, null, 0.15f, null, 0.22f)]
    public void TyreWear_NegativeSentinelPerWheel_MapsToNull(
        float frontLeft, float frontRight, float rearLeft, float rearRight,
        float? expectedFrontLeft, float? expectedFrontRight, float? expectedRearLeft, float? expectedRearRight)
    {
        var sample = MapSample(b => b.WithTyreWear(frontLeft, frontRight, rearLeft, rearRight));

        sample.TyreWear.FrontLeft.ShouldBe(expectedFrontLeft);
        sample.TyreWear.FrontRight.ShouldBe(expectedFrontRight);
        sample.TyreWear.RearLeft.ShouldBe(expectedRearLeft);
        sample.TyreWear.RearRight.ShouldBe(expectedRearRight);
    }

    [Fact]
    public void LapDistanceFraction_NegativeSentinel_MapsToNull()
    {
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) => raw.LapDistanceFraction = -1f));
        sample.TrackPositionFraction.ShouldBeNull();
    }

    [Fact]
    public void LapDistanceFraction_Zero_MapsToZero_NotNull()
    {
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) => raw.LapDistanceFraction = 0f));
        sample.TrackPositionFraction.ShouldBe(0f);
    }

    [Fact]
    public void LapDistanceFraction_PositiveValue_PassesThrough()
    {
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) => raw.LapDistanceFraction = 0.61f));
        sample.TrackPositionFraction.ShouldBe(0.61f);
    }

    // --- Fields that are legitimately negative and must NOT be sentinel-nulled/coerced. ---

    [Fact]
    public void Steering_NegativeValue_PassesThroughUnchanged()
    {
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) => raw.SteerInputRaw = -0.5f));
        sample.Steering.ShouldBe(-0.5f);
    }

    [Fact]
    public void Steering_FullLeft_PassesThroughUnchanged()
    {
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) => raw.SteerInputRaw = -1f));
        sample.Steering.ShouldBe(-1f);
    }

    [Fact]
    public void Gear_Reverse_PassesThroughUnchanged()
    {
        var sample = MapSample(b => b.WithGear(-1));
        sample.Gear.ShouldBe(-1);
    }

    [Fact]
    public void Gear_NotAvailable_PassesThroughUnchanged()
    {
        var sample = MapSample(b => b.WithGear(-2));
        sample.Gear.ShouldBe(-2);
    }

    [Fact]
    public void Gear_Neutral_PassesThroughUnchanged()
    {
        var sample = MapSample(b => b.WithGear(0));
        sample.Gear.ShouldBe(0);
    }

    [Fact]
    public void Gear_ForwardGear_PassesThroughUnchanged()
    {
        var sample = MapSample(b => b.WithGear(4));
        sample.Gear.ShouldBe(4);
    }
}

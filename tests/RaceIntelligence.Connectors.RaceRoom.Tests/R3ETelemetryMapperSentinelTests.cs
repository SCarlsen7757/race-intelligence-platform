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

    // RaceRoom's tire_wear is tread REMAINING (1.0 fresh, falling as the tyre wears), while the
    // canonical TyreWear is wear ACCUMULATED (0 new, 1 fully worn), so the mapper inverts. The
    // sentinel must be filtered before that inversion: -1.0 means "not available", and inverting it
    // first yields a confident, fictional 2.0.
    [Theory]
    [InlineData(-1f, -1f, -1f, -1f, null, null, null, null)]
    [InlineData(1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f)] // fresh tyres -> no wear
    [InlineData(0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f)] // no tread left -> fully worn
    [InlineData(-1f, 0.85f, -1f, 0.78f, null, 0.15f, null, 0.22f)]
    public void TyreWear_IsInvertedFromTreadRemaining_AndSentinelsMapToNull(
        float frontLeft, float frontRight, float rearLeft, float rearRight,
        float? expectedFrontLeft, float? expectedFrontRight, float? expectedRearLeft, float? expectedRearRight)
    {
        var sample = MapSample(b => b.WithTyreWear(frontLeft, frontRight, rearLeft, rearRight));

        ShouldBeWear(sample.TyreWear.FrontLeft, expectedFrontLeft);
        ShouldBeWear(sample.TyreWear.FrontRight, expectedFrontRight);
        ShouldBeWear(sample.TyreWear.RearLeft, expectedRearLeft);
        ShouldBeWear(sample.TyreWear.RearRight, expectedRearRight);
    }

    /// <summary>
    /// Compares an optional wear value with a tolerance. <c>1f - 0.85f</c> is 0.15000004, not
    /// 0.15 — subtracting from one is exactly where float noise shows up, so an exact comparison
    /// would fail on arithmetic rather than on behaviour.
    /// </summary>
    private static void ShouldBeWear(float? actual, float? expected)
    {
        if (expected is null)
        {
            actual.ShouldBeNull();
            return;
        }

        actual.ShouldNotBeNull().ShouldBe(expected.Value, tolerance: 1e-6);
    }

    [Fact]
    public void TyreWear_IncreasesAsTheTyreWearsDown()
    {
        // The property that actually matters downstream: a degradation model fits a slope against
        // this field, so a value that falls over a stint inverts the sign of every wear rate. Real
        // telemetry from a 24-lap stint moved from 0.9979 to 0.8098 tread remaining.
        var fresh = MapSample(b => b.WithTyreWear(0.9979f, 0.9979f, 0.9977f, 0.9977f));
        var worn = MapSample(b => b.WithTyreWear(0.8098f, 0.8098f, 0.8195f, 0.8195f));

        worn.TyreWear.FrontLeft.ShouldNotBeNull().ShouldBeGreaterThan(fresh.TyreWear.FrontLeft!.Value);
        worn.TyreWear.RearLeft.ShouldNotBeNull().ShouldBeGreaterThan(fresh.TyreWear.RearLeft!.Value);
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

    [Theory]
    [InlineData(-1)] // reverse
    [InlineData(0)]  // neutral
    [InlineData(4)]  // fourth forward gear
    public void Gear_RealGear_PassesThroughUnchanged(int gear)
    {
        var sample = MapSample(b => b.WithGear(gear));
        sample.Gear.ShouldBe(gear);
    }

    [Fact]
    public void Gear_NotAvailable_BecomesNull()
    {
        // -2 is RaceRoom's "not available", and the one gear value that is a sentinel rather than a
        // real gear. Reverse is -1, so this cannot be a NullIfNegative case.
        var sample = MapSample(b => b.WithGear(-2));
        sample.Gear.ShouldBeNull();
    }
}

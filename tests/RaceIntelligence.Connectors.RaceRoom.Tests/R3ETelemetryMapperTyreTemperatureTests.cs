using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Regression tests for tyre temperature sentinel handling.
/// </summary>
/// <remarks>
/// Tread and operating-window temperatures use the same <c>-1.0 = N/A</c> convention as the rest
/// of the shared memory block. Before this was fixed, the mapper passed them straight into a
/// non-nullable <see cref="TyreTemperature"/>, so an unavailable reading surfaced as a literal
/// -1 °C — indistinguishable from a real, very cold tyre, and permanently recorded that way
/// because raw telemetry is never rewritten.
/// </remarks>
public class R3ETelemetryMapperTyreTemperatureTests
{
    private static TelemetrySample MapSample(Action<R3ESharedRawBuilder> configure)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Tyre Temp Track", "Tyre Temp Layout");
        configure(builder);
        var raw = builder.Build();
        return R3ETelemetryMapper.ToSample(in raw, Guid.NewGuid(), sequenceNumber: 0, DateTimeOffset.UtcNow);
    }

    private static void SetTyreTemp(
        ref R3ESharedRaw raw,
        int wheel,
        float inner,
        float middle,
        float outer,
        float optimal,
        float cold,
        float hot)
    {
        raw.TireTemp[wheel].CurrentTemp[0] = inner;
        raw.TireTemp[wheel].CurrentTemp[1] = middle;
        raw.TireTemp[wheel].CurrentTemp[2] = outer;
        raw.TireTemp[wheel].OptimalTemp = optimal;
        raw.TireTemp[wheel].ColdTemp = cold;
        raw.TireTemp[wheel].HotTemp = hot;
    }

    [Fact]
    public void TreadTemperatures_NegativeSentinel_MapToNull()
    {
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) =>
            SetTyreTemp(ref raw, 0, -1f, -1f, -1f, -1f, -1f, -1f)));

        var frontLeft = sample.TyreTemperature.FrontLeft;
        frontLeft.Inner.ShouldBeNull();
        frontLeft.Middle.ShouldBeNull();
        frontLeft.Outer.ShouldBeNull();
        frontLeft.Optimal.ShouldBeNull();
        frontLeft.Cold.ShouldBeNull();
        frontLeft.Hot.ShouldBeNull();
    }

    [Fact]
    public void TreadTemperatures_RealReadings_PassThroughUnchanged()
    {
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) =>
            SetTyreTemp(ref raw, 0, 82.5f, 88.25f, 91f, 90f, 70f, 110f)));

        var frontLeft = sample.TyreTemperature.FrontLeft;
        frontLeft.Inner.ShouldBe(82.5f);
        frontLeft.Middle.ShouldBe(88.25f);
        frontLeft.Outer.ShouldBe(91f);
        frontLeft.Optimal.ShouldBe(90f);
        frontLeft.Cold.ShouldBe(70f);
        frontLeft.Hot.ShouldBe(110f);
    }

    [Fact]
    public void TreadTemperature_OfZero_StaysZeroAndIsNotNull()
    {
        // A tyre genuinely reading 0 °C is meaningfully different from one reporting nothing.
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) =>
            SetTyreTemp(ref raw, 0, 0f, 0f, 0f, 90f, 70f, 110f)));

        var frontLeft = sample.TyreTemperature.FrontLeft;
        frontLeft.Inner.ShouldBe(0f);
        frontLeft.Middle.ShouldBe(0f);
        frontLeft.Outer.ShouldBe(0f);
    }

    [Fact]
    public void PartiallyAvailableReadings_NullOnlyTheUnavailableFields()
    {
        // The window may be reported while live tread temps are not, or vice versa — each field
        // is independent, so a single unavailable value must not discard the others.
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) =>
            SetTyreTemp(ref raw, 0, 85f, -1f, 87f, -1f, 70f, 110f)));

        var frontLeft = sample.TyreTemperature.FrontLeft;
        frontLeft.Inner.ShouldBe(85f);
        frontLeft.Middle.ShouldBeNull();
        frontLeft.Outer.ShouldBe(87f);
        frontLeft.Optimal.ShouldBeNull();
        frontLeft.Cold.ShouldBe(70f);
        frontLeft.Hot.ShouldBe(110f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachWheelIsMappedIndependently(int wheel)
    {
        // Distinct value per wheel proves the mapper isn't reusing one wheel's reading for all four.
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            for (var i = 0; i < 4; i++)
            {
                SetTyreTemp(ref raw, i, 10f + i, 20f + i, 30f + i, 90f, 70f, 110f);
            }
        }));

        var actual = sample.TyreTemperature[(WheelPosition)wheel];
        actual.Inner.ShouldBe(10f + wheel);
        actual.Middle.ShouldBe(20f + wheel);
        actual.Outer.ShouldBe(30f + wheel);
    }
}

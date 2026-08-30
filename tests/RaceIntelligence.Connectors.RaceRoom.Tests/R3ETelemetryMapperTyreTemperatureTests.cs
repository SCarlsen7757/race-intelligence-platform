using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Regression tests for tyre temperature sentinel handling and tread orientation.
/// </summary>
/// <remarks>
/// <para>
/// Tread and operating-window temperatures use the same <c>-1.0 = N/A</c> convention as the rest
/// of the shared memory block. Before this was fixed, the mapper passed them straight into a
/// non-nullable <see cref="TyreTemperature"/>, so an unavailable reading surfaced as a literal
/// -1 °C — indistinguishable from a real, very cold tyre, and permanently recorded that way
/// because raw telemetry is never rewritten.
/// </para>
/// <para>
/// <b>The raw slots are left, centre and right across the tyre — not inner, middle and outer.</b>
/// Which edge is inboard depends on which side of the car the tyre is fitted to, and the helper
/// below is named for the raw array deliberately: naming it <c>inner/outer</c> is what let the
/// original inversion be written down as an assertion and look correct.
/// </para>
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

    /// <param name="left">The tyre's left edge — RaceRoom's <c>CurrentTemp[0]</c>. Inboard only on the right of the car.</param>
    /// <param name="centre">The middle of the tread.</param>
    /// <param name="right">The tyre's right edge — RaceRoom's <c>CurrentTemp[2]</c>. Inboard only on the left of the car.</param>
    private static void SetTyreTemp(
        ref R3ESharedRaw raw,
        int wheel,
        float left,
        float centre,
        float right,
        float optimal,
        float cold,
        float hot)
    {
        raw.TireTemp[wheel].CurrentTemp[0] = left;
        raw.TireTemp[wheel].CurrentTemp[1] = centre;
        raw.TireTemp[wheel].CurrentTemp[2] = right;
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

        // Front-left is on the left of the car, so the tyre's left edge (82.5) is its OUTER
        // shoulder and its right edge (91) is the inboard one.
        var frontLeft = sample.TyreTemperature.FrontLeft;
        frontLeft.Outer.ShouldBe(82.5f);
        frontLeft.Middle.ShouldBe(88.25f);
        frontLeft.Inner.ShouldBe(91f);
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
        frontLeft.Outer.ShouldBe(85f);
        frontLeft.Middle.ShouldBeNull();
        frontLeft.Inner.ShouldBe(87f);
        frontLeft.Optimal.ShouldBeNull();
        frontLeft.Cold.ShouldBe(70f);
        frontLeft.Hot.ShouldBe(110f);
    }

    [Fact]
    public void LeftAndRightTyres_ResolveInboardEdgeFromOppositeSlots()
    {
        // The bug this file exists to prevent: RaceRoom reports left, centre and right across the
        // tyre, and mapping slot 0 to Inner for all four wheels puts the left-hand shoulders the
        // wrong way round. Every wheel is given the same raw values, so anything that maps them
        // identically fails here.
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            for (var i = 0; i < 4; i++)
            {
                SetTyreTemp(ref raw, i, left: 70f, centre: 80f, right: 90f, optimal: 90f, cold: 70f, hot: 110f);
            }
        }));

        // Right-hand tyres: the tyre's left edge faces the middle of the car.
        sample.TyreTemperature.FrontRight.Inner.ShouldBe(70f);
        sample.TyreTemperature.FrontRight.Outer.ShouldBe(90f);
        sample.TyreTemperature.RearRight.Inner.ShouldBe(70f);
        sample.TyreTemperature.RearRight.Outer.ShouldBe(90f);

        // Left-hand tyres: the tyre's right edge does.
        sample.TyreTemperature.FrontLeft.Inner.ShouldBe(90f);
        sample.TyreTemperature.FrontLeft.Outer.ShouldBe(70f);
        sample.TyreTemperature.RearLeft.Inner.ShouldBe(90f);
        sample.TyreTemperature.RearLeft.Outer.ShouldBe(70f);
    }

    [Fact]
    public void NegativeCamber_ReadsAsInnerHotterOnAllFourWheels()
    {
        // The physical check, and the one that would have caught this without anybody reasoning
        // about array indices. A car on negative camber runs its inboard shoulders hotter on every
        // corner; a reading that splits by side is a labelling inversion, not a driving artefact.
        // These are the real proportions measured from a stored lap at Brands Hatch.
        var sample = MapSample(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            // Left-hand tyres: inboard edge is the tyre's right, so that is the hotter number.
            SetTyreTemp(ref raw, 0, left: 88.0f, centre: 89.5f, right: 91.2f, optimal: 90f, cold: 70f, hot: 110f);
            SetTyreTemp(ref raw, 2, left: 80.1f, centre: 81.4f, right: 83.1f, optimal: 90f, cold: 70f, hot: 110f);

            // Right-hand tyres: inboard edge is the tyre's left.
            SetTyreTemp(ref raw, 1, left: 75.3f, centre: 74.1f, right: 73.2f, optimal: 90f, cold: 70f, hot: 110f);
            SetTyreTemp(ref raw, 3, left: 74.9f, centre: 73.6f, right: 72.6f, optimal: 90f, cold: 70f, hot: 110f);
        }));

        foreach (var position in Enum.GetValues<WheelPosition>())
        {
            var tyre = sample.TyreTemperature[position];
            tyre.Inner.ShouldNotBeNull();
            tyre.Outer.ShouldNotBeNull();
            tyre.Inner.Value.ShouldBeGreaterThan(
                tyre.Outer.Value,
                $"{position} should read hotter on its inboard shoulder under negative camber.");
        }
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

        // FL and RL are on the left, so their edges arrive swapped relative to the raw array.
        var isLeftSide = wheel is 0 or 2;
        var actual = sample.TyreTemperature[(WheelPosition)wheel];
        actual.Inner.ShouldBe(isLeftSide ? 30f + wheel : 10f + wheel);
        actual.Middle.ShouldBe(20f + wheel);
        actual.Outer.ShouldBe(isLeftSide ? 10f + wheel : 30f + wheel);
    }
}

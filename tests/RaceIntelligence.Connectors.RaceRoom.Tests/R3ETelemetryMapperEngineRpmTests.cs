using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Verifies <see cref="R3ETelemetryMapper.RadiansPerSecondToRpm"/>, the rad/s -&gt; RPM conversion
/// applied to RaceRoom's <c>engine_rps</c> field before it reaches <see cref="RaceIntelligence.Collector.Abstractions.Telemetry.TelemetrySample.EngineRpm"/>.
/// </summary>
public class R3ETelemetryMapperEngineRpmTests
{
    [Fact]
    public void Zero_RadiansPerSecond_Is_Zero_Rpm()
    {
        R3ETelemetryMapper.RadiansPerSecondToRpm(0f).ShouldBe(0f);
    }

    [Fact]
    public void TwoPi_RadiansPerSecond_Is_60_Rpm()
    {
        // 2*pi rad/s is exactly one revolution per second == 60 revolutions per minute.
        float rpm = R3ETelemetryMapper.RadiansPerSecondToRpm(2f * MathF.PI);
        rpm.ShouldBe(60f, 0.001f);
    }

    [Fact]
    public void RealisticEngineSpeed_ConvertsToExpectedRpm()
    {
        // A realistic ~7000 RPM: 7000 rpm * 2*pi / 60 = engine_rps in rad/s.
        const float ExpectedRpm = 7000f;
        float radiansPerSecond = ExpectedRpm * 2f * MathF.PI / 60f;

        float rpm = R3ETelemetryMapper.RadiansPerSecondToRpm(radiansPerSecond);

        rpm.ShouldBe(ExpectedRpm, 0.01f);
    }

    [Theory]
    [InlineData(104.71976f, 1000f)] // 1000 rpm
    [InlineData(837.7580f, 8000f)] // 8000 rpm, a plausible GT3 redline
    public void KnownRadianValues_ConvertToKnownRpm(float radiansPerSecond, float expectedRpm)
    {
        R3ETelemetryMapper.RadiansPerSecondToRpm(radiansPerSecond).ShouldBe(expectedRpm, 0.01f);
    }
}

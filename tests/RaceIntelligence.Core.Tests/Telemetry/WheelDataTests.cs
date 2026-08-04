using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Core.Tests.Telemetry;

public class WheelDataTests
{
    [Fact]
    public void Indexer_MapsEachWheelPositionToRightComponent()
    {
        var wheel = new WheelData<float>(FrontLeft: 1f, FrontRight: 2f, RearLeft: 3f, RearRight: 4f);

        wheel[WheelPosition.FrontLeft].ShouldBe(1f);
        wheel[WheelPosition.FrontRight].ShouldBe(2f);
        wheel[WheelPosition.RearLeft].ShouldBe(3f);
        wheel[WheelPosition.RearRight].ShouldBe(4f);
    }

    [Fact]
    public void From_MapsSpanElementsInFlFrRlRrOrder()
    {
        float[] source = [10f, 20f, 30f, 40f];

        var wheel = WheelData.From<float>(source);

        wheel.FrontLeft.ShouldBe(10f);
        wheel.FrontRight.ShouldBe(20f);
        wheel.RearLeft.ShouldBe(30f);
        wheel.RearRight.ShouldBe(40f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void From_WrongLength_ThrowsArgumentException(int length)
    {
        var source = new float[length];

        Should.Throw<ArgumentException>(() => WheelData.From<float>(source));
    }
}

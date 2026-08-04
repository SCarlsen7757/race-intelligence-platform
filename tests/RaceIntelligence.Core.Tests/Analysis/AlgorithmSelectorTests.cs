using RaceIntelligence.Core.Analysis;
using RaceIntelligence.Core.Capabilities;
using Shouldly;

namespace RaceIntelligence.Core.Tests.Analysis;

public class AlgorithmSelectorTests
{
    private sealed record FakeAlgorithm(string AlgorithmName, SimCapabilities RequiredCapabilities) : IAlgorithmMetadata
    {
        public Version AlgorithmVersion { get; } = new(1, 0);
    }

    [Fact]
    public void Supported_AlgorithmRequiringNothing_IsAlwaysSelected()
    {
        var algo = new FakeAlgorithm("NoRequirements", SimCapabilities.None);

        var result = AlgorithmSelector.Supported([algo], SimCapabilities.None).ToList();

        result.ShouldContain(algo);
    }

    [Fact]
    public void Supported_AlgorithmRequiringCombinedFlags_SelectedOnlyWhenBothPresent()
    {
        var algo = new FakeAlgorithm("TyreModel", SimCapabilities.TyreWear | SimCapabilities.TyrePressure);

        AlgorithmSelector.Supported([algo], SimCapabilities.TyreWear).ShouldNotContain(algo);
        AlgorithmSelector.Supported([algo], SimCapabilities.TyreWear | SimCapabilities.TyrePressure).ShouldContain(algo);
        AlgorithmSelector.Supported([algo], SimCapabilities.TyreWear | SimCapabilities.TyrePressure | SimCapabilities.Ers).ShouldContain(algo);
    }

    [Fact]
    public void Supported_MixedSet_FiltersToOnlyThoseSatisfied()
    {
        var noReq = new FakeAlgorithm("NoReq", SimCapabilities.None);
        var tyreWear = new FakeAlgorithm("TyreWear", SimCapabilities.TyreWear);
        var ers = new FakeAlgorithm("Ers", SimCapabilities.Ers);
        var tyreAndBrake = new FakeAlgorithm("TyreAndBrake", SimCapabilities.TyreWear | SimCapabilities.BrakeTemperature);

        var available = SimCapabilities.TyreWear | SimCapabilities.BrakeTemperature;

        var result = AlgorithmSelector.Supported([noReq, tyreWear, ers, tyreAndBrake], available).ToList();

        result.ShouldBe([noReq, tyreWear, tyreAndBrake], ignoreOrder: true);
        result.ShouldNotContain(ers);
    }
}

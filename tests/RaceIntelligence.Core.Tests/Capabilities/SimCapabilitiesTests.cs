using RaceIntelligence.Core.Capabilities;
using Shouldly;

namespace RaceIntelligence.Core.Tests.Capabilities;

public class SimCapabilitiesTests
{
    [Fact]
    public void Has_SingleFlag_Present_ReturnsTrue()
    {
        var caps = SimCapabilities.TyreWear;

        caps.Has(SimCapabilities.TyreWear).ShouldBeTrue();
    }

    [Fact]
    public void Has_SingleFlag_Absent_ReturnsFalse()
    {
        var caps = SimCapabilities.TyreWear;

        caps.Has(SimCapabilities.Ers).ShouldBeFalse();
    }

    [Fact]
    public void Has_CombinedFlags_AllPresent_ReturnsTrue()
    {
        var caps = SimCapabilities.TyreWear | SimCapabilities.TyrePressure | SimCapabilities.Drs;

        caps.Has(SimCapabilities.TyreWear | SimCapabilities.TyrePressure).ShouldBeTrue();
    }

    [Fact]
    public void Has_CombinedFlags_PartiallyPresent_ReturnsFalse()
    {
        var caps = SimCapabilities.TyreWear;

        caps.Has(SimCapabilities.TyreWear | SimCapabilities.TyrePressure).ShouldBeFalse();
    }

    [Fact]
    public void Has_None_AlwaysReturnsTrue()
    {
        SimCapabilities.None.Has(SimCapabilities.None).ShouldBeTrue();
        SimCapabilities.TyreWear.Has(SimCapabilities.None).ShouldBeTrue();
        (SimCapabilities.TyreWear | SimCapabilities.Ers).Has(SimCapabilities.None).ShouldBeTrue();
    }
}

using RaceIntelligence.Collector.Mapping;
using RaceIntelligence.Collector.Tests.Support;
using RaceIntelligence.Ingest.Contracts;
using Shouldly;

namespace RaceIntelligence.Collector.Tests.Mapping;

/// <summary>
/// Guards the collector-to-wire direction of the session mapping.
/// </summary>
/// <remarks>
/// <see cref="CollectorRequestMapper"/> builds <see cref="SessionCreateRequest"/> positionally, and
/// the request carries several same-typed parameters in a row — two <see langword="int"/>? rate
/// codes, and a run of four nullable strings for the sim's raw identifiers. Transposing any
/// neighbouring pair compiles cleanly and changes nothing observable until the data is queried
/// months later, by which point the recorded sessions are wrong and unrecoverable. These tests
/// exist to make that transposition fail immediately, so every field is asserted against a value
/// distinct from its neighbours.
/// </remarks>
public sealed class CollectorRequestMapperTests
{
    [Fact]
    public void ToSessionCreateRequest_CarriesDriverIdentityAndRateCodes()
    {
        var session = SessionInfoFactory.Create();

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.SimDriverId.ShouldBe(session.SimDriverId);
        request.PlayerName.ShouldBe(session.PlayerName);
        request.FuelUsageRate.ShouldBe(session.FuelUsageRate);
        request.TyreWearRate.ShouldBe(session.TyreWearRate);
    }

    [Fact]
    public void ToSessionCreateRequest_DoesNotTransposeTheTwoRateCodes()
    {
        // Deliberately asymmetric: if the two arguments were swapped in the mapper, equal values
        // would hide it. 0 also has to survive as 0 rather than collapsing into null — it means
        // "the rate was switched off", which is a real setting and not an absence of information.
        var session = SessionInfoFactory.Create() with { FuelUsageRate = 0, TyreWearRate = 4 };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.FuelUsageRate.ShouldBe(0);
        request.TyreWearRate.ShouldBe(4);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(-1, 3)]
    [InlineData(4, 0)]
    public void ToSessionCreateRequest_CarriesRawRateCodesVerbatim(int fuelUsageRate, int tyreWearRate)
    {
        // The sim's raw codes travel untranslated: -1 = not available, 0 = off, 1-4 = 1x-4x for
        // RaceRoom. The collector performs no analysis, so nothing here may normalize a sentinel.
        var session = SessionInfoFactory.Create() with
        {
            FuelUsageRate = fuelUsageRate,
            TyreWearRate = tyreWearRate,
        };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.FuelUsageRate.ShouldBe(fuelUsageRate);
        request.TyreWearRate.ShouldBe(tyreWearRate);
    }

    [Fact]
    public void ToSessionCreateRequest_LeavesUnknownDriverIdentityNull()
    {
        var session = SessionInfoFactory.Create() with
        {
            SimDriverId = null,
            PlayerName = null,
            FuelUsageRate = null,
            TyreWearRate = null,
        };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.SimDriverId.ShouldBeNull();
        request.PlayerName.ShouldBeNull();
        request.FuelUsageRate.ShouldBeNull();
        request.TyreWearRate.ShouldBeNull();
    }

    [Fact]
    public void ToSessionCreateRequest_DoesNotTransposeTheSimRawIdentifiers()
    {
        var session = SessionInfoFactory.Create() with
        {
            SimDriverId = "driver-1",
            SimCarId = "car-2",
            SimCarClassId = "class-3",
            SimManufacturerId = "manufacturer-4",
        };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.SimDriverId.ShouldBe("driver-1");
        request.SimCarId.ShouldBe("car-2");
        request.SimCarClassId.ShouldBe("class-3");
        request.SimManufacturerId.ShouldBe("manufacturer-4");
    }
}

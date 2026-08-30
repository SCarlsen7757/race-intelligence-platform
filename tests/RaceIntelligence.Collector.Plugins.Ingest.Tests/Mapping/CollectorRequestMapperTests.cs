using System.Text.Json;
using RaceIntelligence.Collector.Plugins.Ingest.Mapping;
using RaceIntelligence.Collector.Plugins.Ingest.Tests.Support;
using RaceIntelligence.Collector.TestSupport;
using RaceIntelligence.Core.Sessions;
using Shouldly;

namespace RaceIntelligence.Collector.Plugins.Ingest.Tests.Mapping;

/// <summary>
/// Covers the Core-to-wire mapping the collector owns. Every assertion here uses a distinct value
/// per field: a positional record constructor makes two adjacent same-typed fields trivially
/// transposable, and a test that reuses the same value for both would pass either way.
/// </summary>
public class CollectorRequestMapperTests
{
    [Fact]
    public void ToSessionCreateRequest_maps_the_three_sim_identifier_strings_to_their_own_fields()
    {
        // SimCarId, SimCarClassId and SimManufacturerId are three consecutive nullable strings at
        // the end of the positional request record -- the single easiest place in this codebase to
        // transpose two arguments without the compiler noticing.
        var session = SessionInfoFactory.Create() with
        {
            SimCarId = "car-2922",
            SimCarClassId = "class-1601",
            SimManufacturerId = "manufacturer-3301",
        };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.SimCarId.ShouldBe("car-2922");
        request.SimCarClassId.ShouldBe("class-1601");
        request.SimManufacturerId.ShouldBe("manufacturer-3301");
    }

    [Fact]
    public void ToSessionCreateRequest_keeps_an_unknown_sim_identifier_null_rather_than_borrowing_a_neighbour()
    {
        // The realistic partial case: a sim that reports a car id but no manufacturer id. A
        // transposition or an accidental fallback would silently fill the null one in.
        var session = SessionInfoFactory.Create() with
        {
            SimCarId = "car-only",
            SimCarClassId = null,
            SimManufacturerId = null,
        };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.SimCarId.ShouldBe("car-only");
        request.SimCarClassId.ShouldBeNull();
        request.SimManufacturerId.ShouldBeNull();
    }

    [Fact]
    public void ToSessionCreateRequest_keeps_the_sim_identifiers_distinct_from_the_human_readable_names()
    {
        // Names and ids are both nullable strings on the same record. RaceRoom populates the ids
        // and leaves the names null; a sim with names would do the opposite. Neither may leak into
        // the other's field.
        var session = SessionInfoFactory.Create() with
        {
            CarName = "name-car",
            CarClassName = "name-class",
            ManufacturerName = "name-manufacturer",
            SimCarId = "id-car",
            SimCarClassId = "id-class",
            SimManufacturerId = "id-manufacturer",
        };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.CarName.ShouldBe("name-car");
        request.CarClassName.ShouldBe("name-class");
        request.ManufacturerName.ShouldBe("name-manufacturer");
        request.SimCarId.ShouldBe("id-car");
        request.SimCarClassId.ShouldBe("id-class");
        request.SimManufacturerId.ShouldBe("id-manufacturer");
    }

    [Fact]
    public void ToSessionCreateRequest_maps_the_identifying_and_track_fields()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var session = SessionInfoFactory.Create(startedAtUtc: startedAt) with
        {
            TrackName = "Track Name",
            LayoutName = "Layout Name",
            LayoutLengthMeters = 4321f,
            SessionType = (SessionType)2,
            PlayerName = "Player Name",
        };

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.SessionId.ShouldBe(session.SessionId);
        request.TrackName.ShouldBe("Track Name");
        request.LayoutName.ShouldBe("Layout Name");
        request.LayoutLengthMeters.ShouldBe(4321f);
        request.SessionType.ShouldBe(2);
        request.StartedAtUtc.ShouldBe(startedAt);
        request.PlayerName.ShouldBe("Player Name");
        request.Capabilities.ShouldBe((ulong)session.Capabilities);
    }

    [Fact]
    public void ToSessionCreateRequest_serializes_populated_extras_and_leaves_an_unset_element_null()
    {
        using var document = JsonDocument.Parse("""{"gameMode":3}""");

        CollectorRequestMapper.ToSessionCreateRequest(SessionInfoFactory.Create() with { Extras = document.RootElement })
            .ExtrasJson.ShouldBe("""{"gameMode":3}""");

        // default(JsonElement) is Undefined, not an empty object -- serializing it would throw.
        CollectorRequestMapper.ToSessionCreateRequest(SessionInfoFactory.Create() with { Extras = default })
            .ExtrasJson.ShouldBeNull();
    }

    [Fact]
    public void ToLapCompletedRequest_maps_every_lap_field_to_its_own_slot()
    {
        var lap = new LapInfo
        {
            SessionId = Guid.NewGuid(),
            LapNumber = 5,
            LapTime = TimeSpan.FromSeconds(91),
            FuelUsed = 2.5f,
            AverageSpeed = 40f,
            MaxSpeed = 70f,
            IsValid = true,
        };

        var request = CollectorRequestMapper.ToLapCompletedRequest(lap);

        request.LapNumber.ShouldBe(5);
        request.LapTime.ShouldBe(TimeSpan.FromSeconds(91));
        request.FuelUsed.ShouldBe(2.5f);
        request.AverageSpeed.ShouldBe(40f);
        request.MaxSpeed.ShouldBe(70f);
        request.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ToLapCompletedRequest_carries_unknown_lap_values_through_as_null()
    {
        var lap = new LapInfo { SessionId = Guid.NewGuid(), LapNumber = 2, IsValid = false };

        var request = CollectorRequestMapper.ToLapCompletedRequest(lap);

        request.LapTime.ShouldBeNull();
        request.FuelUsed.ShouldBeNull();
        request.AverageSpeed.ShouldBeNull();
        request.MaxSpeed.ShouldBeNull();
        request.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void ToSessionEndedRequest_sets_only_the_end_time()
    {
        var endedAt = DateTimeOffset.UtcNow;

        var request = CollectorRequestMapper.ToSessionEndedRequest(endedAt);

        request.EndedAtUtc.ShouldBe(endedAt);

        // Everything else must stay null: on a PATCH, null means "leave unchanged", and the
        // collector performs no analysis so it has nothing else to say about a finished session.
        request.ExtrasJson.ShouldBeNull();
    }

    [Fact]
    public void ToSessionCreateRequest_carries_driver_identity_and_rate_codes()
    {
        var session = SessionInfoFactory.Create();

        var request = CollectorRequestMapper.ToSessionCreateRequest(session);

        request.SimDriverId.ShouldBe(session.SimDriverId);
        request.PlayerName.ShouldBe(session.PlayerName);
        request.FuelUsageRate.ShouldBe(session.FuelUsageRate);
        request.TyreWearRate.ShouldBe(session.TyreWearRate);
    }

    [Fact]
    public void ToSessionCreateRequest_does_not_transpose_the_two_rate_codes()
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
    public void ToSessionCreateRequest_carries_raw_rate_codes_verbatim(int fuelUsageRate, int tyreWearRate)
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
    public void ToSessionCreateRequest_leaves_unknown_driver_identity_null()
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
    public void ToSessionCreateRequest_does_not_transpose_the_sim_raw_identifiers()
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

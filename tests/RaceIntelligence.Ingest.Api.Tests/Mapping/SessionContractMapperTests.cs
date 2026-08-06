using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Ingest.Api.Tests.Support;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Mapping;

/// <summary>Pure, no-Docker unit tests for <see cref="SessionContractMapper"/> and <see cref="GameVersionContractMapper"/>.</summary>
public sealed class SessionContractMapperTests
{
    [Fact]
    public void ToSessionInfo_maps_every_field_including_raw_capability_and_session_type_ints()
    {
        var request = DtoFactory.SessionCreateRequest() with
        {
            Capabilities = (ulong)(SimCapabilities.TyreWear | SimCapabilities.TyrePressure),
            SessionType = (int)SessionType.Qualifying,
        };

        var info = SessionContractMapper.ToSessionInfo(request);

        info.SessionId.ShouldBe(request.SessionId);
        info.GameVersion.Game.Key.ShouldBe(request.GameVersion.GameKey);
        info.GameVersion.ConnectorVersion.ShouldBe(request.GameVersion.ConnectorVersion);
        info.Capabilities.ShouldBe(SimCapabilities.TyreWear | SimCapabilities.TyrePressure);
        info.SessionType.ShouldBe(SessionType.Qualifying);
        info.TrackName.ShouldBe(request.TrackName);
        info.LayoutName.ShouldBe(request.LayoutName);
        info.PlayerName.ShouldBe(request.PlayerName);
        info.CarName.ShouldBe(request.CarName);
    }

    [Fact]
    public void ToSessionInfo_carries_the_sim_driver_id_and_both_rate_codes_through()
    {
        var request = DtoFactory.SessionCreateRequest() with
        {
            SimDriverId = "sim-driver-4711",
            FuelUsageRate = 4,
            TyreWearRate = 2,
        };

        var info = SessionContractMapper.ToSessionInfo(request);

        info.SimDriverId.ShouldBe("sim-driver-4711");
        info.FuelUsageRate.ShouldBe(4);
        info.TyreWearRate.ShouldBe(2);
    }

    [Theory]
    // -1 is RaceRoom's "not available" and 0 its "switched off": both are meaningful raw codes the
    // wire must carry through untouched, and 0 in particular must never collapse into null.
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(0, 4)]
    [InlineData(-1, 0)]
    public void ToSessionInfo_preserves_sentinel_rate_codes_verbatim(int fuelUsageRate, int tyreWearRate)
    {
        var request = DtoFactory.SessionCreateRequest() with
        {
            FuelUsageRate = fuelUsageRate,
            TyreWearRate = tyreWearRate,
        };

        var info = SessionContractMapper.ToSessionInfo(request);

        info.FuelUsageRate.ShouldBe(fuelUsageRate);
        info.TyreWearRate.ShouldBe(tyreWearRate);
    }

    [Fact]
    public void ToSessionInfo_preserves_a_null_sim_driver_id_and_null_rates()
    {
        var request = DtoFactory.SessionCreateRequest() with
        {
            SimDriverId = null,
            FuelUsageRate = null,
            TyreWearRate = null,
        };

        var info = SessionContractMapper.ToSessionInfo(request);

        info.SimDriverId.ShouldBeNull("a sim that reports no driver id must not gain a fabricated one");
        info.FuelUsageRate.ShouldBeNull();
        info.TyreWearRate.ShouldBeNull();
    }

    [Fact]
    public void ToSessionInfo_defaults_extras_to_an_empty_object_when_json_is_null()
    {
        var request = DtoFactory.SessionCreateRequest() with { ExtrasJson = null };

        var info = SessionContractMapper.ToSessionInfo(request);

        info.Extras.GetRawText().ShouldBe("{}");
    }

    [Fact]
    public void ToSessionInfo_parses_supplied_extras_json()
    {
        var request = DtoFactory.SessionCreateRequest() with { ExtrasJson = """{"pushToPass":true}""" };

        var info = SessionContractMapper.ToSessionInfo(request);

        info.Extras.GetProperty("pushToPass").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void ToLapInfo_never_accepts_a_quality_score_from_the_wire()
    {
        var request = new LapCompletedRequest(SchemaVersion.Current, LapNumber: 3, LapTime: TimeSpan.FromMinutes(1.5), FuelUsed: 2f, AverageSpeed: 40f, MaxSpeed: 60f, IsValid: true);
        var sessionId = Guid.CreateVersion7();

        var lap = SessionContractMapper.ToLapInfo(sessionId, request);

        lap.SessionId.ShouldBe(sessionId);
        lap.LapNumber.ShouldBe(3);
        lap.LapTime.ShouldBe(TimeSpan.FromMinutes(1.5));
        lap.IsValid.ShouldBeTrue();
        lap.QualityScore.ShouldBeNull("the collector performs no analysis; quality score is computed later");
    }

    [Fact]
    public void GameVersionContractMapper_round_trips_through_core()
    {
        var dto = DtoFactory.UniqueGameVersion();

        var core = GameVersionContractMapper.ToCore(dto);
        var roundTripped = GameVersionContractMapper.ToDto(core);

        roundTripped.ShouldBe(dto);
    }

    [Fact]
    public void GameVersionContractMapper_preserves_a_null_game_version_string()
    {
        var dto = DtoFactory.UniqueGameVersion() with { GameVersion = null };

        var core = GameVersionContractMapper.ToCore(dto);

        core.GameVersion.ShouldBeNull();
    }
}

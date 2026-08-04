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

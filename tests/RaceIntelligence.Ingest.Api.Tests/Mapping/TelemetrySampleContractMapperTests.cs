using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Ingest.Api.Tests.Support;
using RaceIntelligence.Ingest.Contracts.Mapping;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Mapping;

/// <summary>
/// Pure, no-Docker unit tests for <see cref="TelemetrySampleContractMapper"/>: the DTO/Core
/// round-trip both the collector and the ingest API rely on.
/// </summary>
public sealed class TelemetrySampleContractMapperTests
{
    [Fact]
    public void RoundTrip_preserves_every_field()
    {
        var sessionId = Guid.CreateVersion7();
        var dto = DtoFactory.TelemetrySample(sessionId, sequenceNumber: 42);

        var core = TelemetrySampleContractMapper.ToCore(dto);
        var roundTripped = TelemetrySampleContractMapper.ToDto(core);

        roundTripped.ShouldBe(dto);
    }

    [Fact]
    public void Null_throttle_stays_null_through_the_round_trip()
    {
        var dto = DtoFactory.TelemetrySample(Guid.CreateVersion7(), 1) with { Throttle = null };

        var core = TelemetrySampleContractMapper.ToCore(dto);
        core.Throttle.ShouldBeNull("a null (unavailable) throttle reading must never be coerced to a real value");

        var roundTripped = TelemetrySampleContractMapper.ToDto(core);
        roundTripped.Throttle.ShouldBeNull();
    }

    [Fact]
    public void Zero_throttle_stays_exactly_zero_through_the_round_trip()
    {
        // Zero is a real, valid "no throttle input" reading -- it must be distinguishable from
        // "the source doesn't report throttle at all" (null). Collapsing null -> 0 (or vice versa)
        // would silently corrupt permanently-stored raw telemetry.
        var dto = DtoFactory.TelemetrySample(Guid.CreateVersion7(), 1) with { Throttle = 0f };

        var core = TelemetrySampleContractMapper.ToCore(dto);
        core.Throttle.ShouldNotBeNull();
        core.Throttle!.Value.ShouldBe(0f);

        var roundTripped = TelemetrySampleContractMapper.ToDto(core);
        roundTripped.Throttle.ShouldNotBeNull();
        roundTripped.Throttle!.Value.ShouldBe(0f);
    }

    [Fact]
    public void Null_per_wheel_tyre_pressure_and_wear_survive_the_round_trip()
    {
        var dto = DtoFactory.TelemetrySample(Guid.CreateVersion7(), 1) with
        {
            TyrePressureFrontLeft = null,
            TyrePressureFrontRight = null,
            TyrePressureRearLeft = null,
            TyrePressureRearRight = null,
            TyreWearFrontLeft = null,
            TyreWearFrontRight = null,
            TyreWearRearLeft = null,
            TyreWearRearRight = null,
        };

        var core = TelemetrySampleContractMapper.ToCore(dto);

        core.TyrePressure.FrontLeft.ShouldBeNull();
        core.TyrePressure.FrontRight.ShouldBeNull();
        core.TyrePressure.RearLeft.ShouldBeNull();
        core.TyrePressure.RearRight.ShouldBeNull();
        core.TyreWear.FrontLeft.ShouldBeNull();
        core.TyreWear.FrontRight.ShouldBeNull();
        core.TyreWear.RearLeft.ShouldBeNull();
        core.TyreWear.RearRight.ShouldBeNull();
    }

    [Fact]
    public void Wheel_ordering_is_preserved_FL_FR_RL_RR()
    {
        var dto = DtoFactory.TelemetrySample(Guid.CreateVersion7(), 1) with
        {
            WheelSpeedFrontLeft = 10f,
            WheelSpeedFrontRight = 20f,
            WheelSpeedRearLeft = 30f,
            WheelSpeedRearRight = 40f,
        };

        var core = TelemetrySampleContractMapper.ToCore(dto);

        core.WheelSpeed.FrontLeft.ShouldBe(10f);
        core.WheelSpeed.FrontRight.ShouldBe(20f);
        core.WheelSpeed.RearLeft.ShouldBe(30f);
        core.WheelSpeed.RearRight.ShouldBe(40f);
    }

    [Fact]
    public void Extras_json_round_trips_through_the_raw_string_form()
    {
        var dto = DtoFactory.TelemetrySample(Guid.CreateVersion7(), 1) with { Extras = """{"pushToPass":{"usesRemaining":5}}""" };

        var core = TelemetrySampleContractMapper.ToCore(dto);

        // Carried verbatim in both directions now — the mapper neither parses nor reformats it.
        core.Extras.ShouldBe(dto.Extras);

        var roundTripped = TelemetrySampleContractMapper.ToDto(core);
        roundTripped.Extras.ShouldBe(dto.Extras);
    }

    [Fact]
    public void ToCore_and_ToDto_are_symmetric_for_a_hand_built_core_sample()
    {
        var sessionId = Guid.CreateVersion7();
        var core = new TelemetrySample
        {
            SessionId = sessionId,
            SequenceNumber = 7,
            Timestamp = DateTimeOffset.UnixEpoch,
            SimulationTime = 3.5,
            Speed = 55f,
            Throttle = null,
            Brake = 1f,
            Steering = -0.3f,
            Gear = 5,
            EngineRpm = 7000f,
            FuelLeft = 12.5f,
            LapNumber = 3,
            Sector = 2,
            Position = null,
            WheelSpeed = new WheelData<float>(1, 2, 3, 4),
            SuspensionTravel = new WheelData<float>(0.1f, 0.1f, 0.1f, 0.1f),
            TyreTemperature = new WheelData<TyreTemperature>(
                new TyreTemperature(80, 82, 84, 90, 70, 110),
                new TyreTemperature(80, 82, 84, 90, 70, 110),
                new TyreTemperature(80, 82, 84, 90, 70, 110),
                new TyreTemperature(80, 82, 84, 90, 70, 110)),
            TyrePressure = default,
            TyreWear = default,
            TrackPositionFraction = null,
            Extras = """{"pushToPass":{"usesRemaining":5}}""",
        };

        var dto = TelemetrySampleContractMapper.ToDto(core);
        var roundTripped = TelemetrySampleContractMapper.ToCore(dto);

        // Extras is text, so it has value equality like every other member and the record's own
        // structural comparison covers the whole sample in one assertion.
        roundTripped.ShouldBe(core);
    }
}

using System.Text.Json;
using RaceIntelligence.Ingest.Contracts.Telemetry;
using CoreTelemetry = RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Ingest.Contracts.Mapping;

/// <summary>
/// Converts <see cref="TelemetrySampleDto"/> to/from the canonical
/// <see cref="CoreTelemetry.TelemetrySample"/>, so the collector (writing the wire format) and the
/// ingest API (reading it) share exactly one conversion.
/// </summary>
/// <remarks>
/// Every conversion here is a straight field-for-field copy, deliberately: no defaulting, no
/// coercion. In particular a <see langword="null"/> nullable field (e.g. <c>Throttle</c>) must stay
/// <see langword="null"/> through both directions of this mapping — collapsing "unavailable" to
/// <c>0</c> would corrupt raw telemetry that is stored permanently. See the remarks on
/// <see cref="CoreTelemetry.TelemetrySample"/> for the full rationale.
/// </remarks>
public static class TelemetrySampleContractMapper
{
    /// <summary>Converts a canonical telemetry sample into its wire DTO form.</summary>
    public static TelemetrySampleDto ToDto(CoreTelemetry.TelemetrySample sample) => new()
    {
        SessionId = sample.SessionId,
        SequenceNumber = sample.SequenceNumber,
        Timestamp = sample.Timestamp,
        SimulationTime = sample.SimulationTime,
        Speed = sample.Speed,
        Throttle = sample.Throttle,
        Brake = sample.Brake,
        Steering = sample.Steering,
        Gear = sample.Gear,
        EngineRpm = sample.EngineRpm,
        FuelLeft = sample.FuelLeft,
        LapNumber = sample.LapNumber,
        Sector = sample.Sector,
        Position = sample.Position,
        WheelSpeedFrontLeft = sample.WheelSpeed.FrontLeft,
        WheelSpeedFrontRight = sample.WheelSpeed.FrontRight,
        WheelSpeedRearLeft = sample.WheelSpeed.RearLeft,
        WheelSpeedRearRight = sample.WheelSpeed.RearRight,
        SuspensionTravelFrontLeft = sample.SuspensionTravel.FrontLeft,
        SuspensionTravelFrontRight = sample.SuspensionTravel.FrontRight,
        SuspensionTravelRearLeft = sample.SuspensionTravel.RearLeft,
        SuspensionTravelRearRight = sample.SuspensionTravel.RearRight,
        TyrePressureFrontLeft = sample.TyrePressure.FrontLeft,
        TyrePressureFrontRight = sample.TyrePressure.FrontRight,
        TyrePressureRearLeft = sample.TyrePressure.RearLeft,
        TyrePressureRearRight = sample.TyrePressure.RearRight,
        TyreWearFrontLeft = sample.TyreWear.FrontLeft,
        TyreWearFrontRight = sample.TyreWear.FrontRight,
        TyreWearRearLeft = sample.TyreWear.RearLeft,
        TyreWearRearRight = sample.TyreWear.RearRight,
        TyreTemperatureFrontLeft = ToDto(sample.TyreTemperature.FrontLeft),
        TyreTemperatureFrontRight = ToDto(sample.TyreTemperature.FrontRight),
        TyreTemperatureRearLeft = ToDto(sample.TyreTemperature.RearLeft),
        TyreTemperatureRearRight = ToDto(sample.TyreTemperature.RearRight),
        TrackPositionFraction = sample.TrackPositionFraction,
        Extras = SerializeExtras(sample.Extras),
    };

    /// <summary>Converts a wire telemetry sample DTO back into its canonical Core form.</summary>
    public static CoreTelemetry.TelemetrySample ToCore(TelemetrySampleDto dto) => new()
    {
        SessionId = dto.SessionId,
        SequenceNumber = dto.SequenceNumber,
        Timestamp = dto.Timestamp,
        SimulationTime = dto.SimulationTime,
        Speed = dto.Speed,
        Throttle = dto.Throttle,
        Brake = dto.Brake,
        Steering = dto.Steering,
        Gear = dto.Gear,
        EngineRpm = dto.EngineRpm,
        FuelLeft = dto.FuelLeft,
        LapNumber = dto.LapNumber,
        Sector = dto.Sector,
        Position = dto.Position,
        WheelSpeed = new CoreTelemetry.WheelData<float>(
            dto.WheelSpeedFrontLeft, dto.WheelSpeedFrontRight, dto.WheelSpeedRearLeft, dto.WheelSpeedRearRight),
        SuspensionTravel = new CoreTelemetry.WheelData<float>(
            dto.SuspensionTravelFrontLeft, dto.SuspensionTravelFrontRight, dto.SuspensionTravelRearLeft, dto.SuspensionTravelRearRight),
        TyrePressure = new CoreTelemetry.WheelData<float?>(
            dto.TyrePressureFrontLeft, dto.TyrePressureFrontRight, dto.TyrePressureRearLeft, dto.TyrePressureRearRight),
        TyreWear = new CoreTelemetry.WheelData<float?>(
            dto.TyreWearFrontLeft, dto.TyreWearFrontRight, dto.TyreWearRearLeft, dto.TyreWearRearRight),
        TyreTemperature = new CoreTelemetry.WheelData<CoreTelemetry.TyreTemperature>(
            ToCore(dto.TyreTemperatureFrontLeft),
            ToCore(dto.TyreTemperatureFrontRight),
            ToCore(dto.TyreTemperatureRearLeft),
            ToCore(dto.TyreTemperatureRearRight)),
        TrackPositionFraction = dto.TrackPositionFraction,
        Extras = DeserializeExtras(dto.Extras),
    };

    private static TyreTemperatureDto ToDto(CoreTelemetry.TyreTemperature t) =>
        new() { Inner = t.Inner, Middle = t.Middle, Outer = t.Outer, Optimal = t.Optimal, Cold = t.Cold, Hot = t.Hot };

    private static CoreTelemetry.TyreTemperature ToCore(TyreTemperatureDto dto) =>
        new(dto.Inner, dto.Middle, dto.Outer, dto.Optimal, dto.Cold, dto.Hot);

    /// <summary>
    /// Serializes <see cref="CoreTelemetry.TelemetrySample.Extras"/> to the raw JSON text carried
    /// on the wire. An <see cref="JsonValueKind.Undefined"/> element (an uninitialized
    /// <c>required</c> property that was never explicitly set) is written as JSON <c>null</c>
    /// rather than throwing — MessagePack has no concept of "no value at all" for a required
    /// string member.
    /// </summary>
    private static string SerializeExtras(JsonElement extras) =>
        extras.ValueKind == JsonValueKind.Undefined ? "null" : extras.GetRawText();

    /// <summary>
    /// Parses the wire <see cref="TelemetrySampleDto.Extras"/> JSON text back into a
    /// <see cref="JsonElement"/>.
    /// </summary>
    /// <remarks>
    /// The element is cloned so its parent document can be disposed here. Returning
    /// <c>JsonDocument.Parse(json).RootElement</c> directly would keep every parsed document alive
    /// — 60 undisposed documents a second per session, each holding array-pool buffers the pool
    /// never gets back.
    /// </remarks>
    private static JsonElement DeserializeExtras(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

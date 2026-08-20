using System.Text.Json;
using CoreTelemetry = RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Persistence.Mapping;

/// <summary>Maps the canonical <see cref="CoreTelemetry.TelemetrySample"/> to its persisted row shape.</summary>
/// <remarks>
/// <see cref="ToEntity"/> is the EF path, used for tests and ad-hoc writes. The bulk
/// <c>Bulk/NpgsqlTelemetryWriter</c> deliberately does not go through it — it writes the same field
/// values straight into the binary <c>COPY</c> stream — but it does share this type's per-field
/// conversion rules (<see cref="ToSmallInt"/>, <see cref="SerializeTyreTemperatureText"/>, and the
/// all-null-means-null-column rule on the nullable wheel arrays), so the two paths cannot drift.
/// </remarks>
public static class TelemetrySampleMapper
{
    /// <summary>Converts a canonical telemetry sample into its EF entity form.</summary>
    public static Entities.TelemetrySample ToEntity(CoreTelemetry.TelemetrySample sample) => new()
    {
        SessionId = sample.SessionId,
        Timestamp = sample.Timestamp,
        SequenceNumber = sample.SequenceNumber,
        SimulationTime = sample.SimulationTime,
        LapNumber = sample.LapNumber,
        Sector = sample.Sector,
        Speed = sample.Speed,
        Throttle = sample.Throttle,
        Brake = sample.Brake,
        Clutch = sample.Clutch,
        Steering = sample.Steering,
        Gear = sample.Gear.HasValue ? ToSmallInt(sample.Gear.Value) : null,
        EngineRpm = sample.EngineRpm,
        FuelLeft = sample.FuelLeft,
        Position = sample.Position.HasValue ? ToSmallInt(sample.Position.Value) : null,
        TrackPositionFraction = sample.TrackPositionFraction,
        WheelSpeed = ToArray(sample.WheelSpeed),
        SuspensionTravel = ToArray(sample.SuspensionTravel),
        TyrePressure = ToNullableArray(sample.TyrePressure),
        TyreWear = ToNullableArray(sample.TyreWear),
        TyreTemperature = SerializeTyreTemperature(sample.TyreTemperature),
        Extras = sample.Extras,
    };

    /// <summary>Converts a per-wheel required value into FL/FR/RL/RR array order for the <c>real[]</c> columns.</summary>
    public static float[] ToArray(CoreTelemetry.WheelData<float> wheelData) =>
        [wheelData.FrontLeft, wheelData.FrontRight, wheelData.RearLeft, wheelData.RearRight];

    /// <summary>
    /// Converts a per-wheel optional value into FL/FR/RL/RR array order. Returns
    /// <see langword="null"/> (rather than an all-null array) when every wheel is
    /// <see langword="null"/>, matching the nullable-column semantics documented on
    /// <see cref="Entities.TelemetrySample.TyrePressure"/> / <see cref="Entities.TelemetrySample.TyreWear"/>.
    /// </summary>
    public static float?[]? ToNullableArray(CoreTelemetry.WheelData<float?> wheelData)
    {
        if (wheelData is { FrontLeft: null, FrontRight: null, RearLeft: null, RearRight: null })
        {
            return null;
        }

        return [wheelData.FrontLeft, wheelData.FrontRight, wheelData.RearLeft, wheelData.RearRight];
    }

    /// <summary>
    /// Serializes per-wheel tyre temperature detail into the jsonb shape stored in
    /// <c>telemetry_samples.tyre_temperature</c>: an object keyed by wheel position, each holding
    /// inner/middle/outer/optimal/cold/hot readings.
    /// </summary>
    /// <remarks>
    /// Only the EF entity path needs a <see cref="JsonElement"/>; the bulk <c>COPY</c> path calls
    /// <see cref="SerializeTyreTemperatureText"/> and skips the document entirely. The element is
    /// cloned off a disposed document so the parse buffers go back to the pool.
    /// </remarks>
    public static JsonElement SerializeTyreTemperature(CoreTelemetry.WheelData<CoreTelemetry.TyreTemperature> tyreTemperature)
    {
        using var document = JsonDocument.Parse(SerializeTyreTemperatureText(tyreTemperature));
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Serializes per-wheel tyre temperature detail straight to the jsonb text Postgres stores,
    /// which is all the binary <c>COPY</c> path ever wanted — going via a
    /// <see cref="JsonElement"/> only to read its raw text back out again cost a parse, a document
    /// and a second string per sample, 60 times a second.
    /// </summary>
    public static string SerializeTyreTemperatureText(CoreTelemetry.WheelData<CoreTelemetry.TyreTemperature> tyreTemperature) =>
        JsonSerializer.Serialize(new TyreTemperatureSetDto(
            ToDto(tyreTemperature.FrontLeft),
            ToDto(tyreTemperature.FrontRight),
            ToDto(tyreTemperature.RearLeft),
            ToDto(tyreTemperature.RearRight)));

    private static TyreTemperatureDto ToDto(CoreTelemetry.TyreTemperature t) =>
        new(t.Inner, t.Middle, t.Outer, t.Optimal, t.Cold, t.Hot);

    /// <summary>
    /// Narrows a Core <see cref="int"/> gear/position value to the <c>smallint</c> the column is
    /// stored as. Gear and race position never realistically approach <see cref="short.MaxValue"/>,
    /// but an out-of-range value indicates upstream connector corruption that should fail loudly
    /// rather than silently wrap.
    /// </summary>
    /// <summary>
    /// Narrows a canonical <see cref="int"/> to the <c>smallint</c> its column is, checked.
    /// </summary>
    /// <remarks>
    /// Public rather than internal because a simulator's bulk writer lives in its own assembly now
    /// and shares this rule with <see cref="ToEntity"/> — which is the point: an unchecked narrowing
    /// would turn an out-of-range sim code into a plausible one, and the two write paths agreeing on
    /// that is what stops them drifting.
    /// </remarks>
    public static short ToSmallInt(int value) => checked((short)value);

    /// <remarks>
    /// Nullable throughout: a simulator that does not report a given temperature must round-trip as
    /// JSON <c>null</c>, never as a sentinel that would later read back as a real sub-zero value.
    /// </remarks>
    private sealed record TyreTemperatureDto(float? Inner, float? Middle, float? Outer, float? Optimal, float? Cold, float? Hot);

    private sealed record TyreTemperatureSetDto(
        TyreTemperatureDto FrontLeft,
        TyreTemperatureDto FrontRight,
        TyreTemperatureDto RearLeft,
        TyreTemperatureDto RearRight);
}

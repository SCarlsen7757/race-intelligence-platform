using MessagePack;

namespace RaceIntelligence.Ingest.Contracts.Telemetry;

/// <summary>
/// Wire form of <see cref="RaceIntelligence.Core.Telemetry.TelemetrySample"/>, the hot-path
/// (50-60 Hz) canonical telemetry snapshot, serialized with MessagePack rather than JSON.
/// </summary>
/// <remarks>
/// <para>
/// This DTO deliberately does not mirror <c>TelemetrySample</c> field-for-field:
/// <see cref="RaceIntelligence.Core.Telemetry.WheelData{T}"/> and
/// <see cref="RaceIntelligence.Core.Telemetry.TyreTemperature"/> are generic/structural types that
/// don't map to a stable MessagePack contract, so each <c>WheelData&lt;T&gt;</c> group is flattened
/// into four explicit <c>FrontLeft</c>/<c>FrontRight</c>/<c>RearLeft</c>/<c>RearRight</c> members
/// (FL, FR, RL, RR — matching the ordering used throughout the platform), and per-wheel tyre
/// temperature becomes four <see cref="TyreTemperatureDto"/> members. Similarly,
/// <see cref="System.Text.Json.JsonElement"/> has no native MessagePack representation, so
/// <see cref="Extras"/> is carried as a raw JSON string instead.
/// </para>
/// <para>
/// Use <c>Mapping.TelemetrySampleContractMapper</c> to convert to/from the Core
/// <see cref="RaceIntelligence.Core.Telemetry.TelemetrySample"/> — both the collector and the
/// ingest API share that single conversion so the flattening logic exists exactly once.
/// </para>
/// </remarks>
[MessagePackObject]
public sealed record TelemetrySampleDto
{
    /// <summary>The session this sample belongs to.</summary>
    [Key(0)]
    public required Guid SessionId { get; init; }

    /// <summary>Collector-assigned, monotonically increasing (per session) sequence number.</summary>
    [Key(1)]
    public required long SequenceNumber { get; init; }

    /// <summary>Wall-clock time the sample was captured.</summary>
    [Key(2)]
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Simulator-reported in-session time, in seconds, since the session began.</summary>
    [Key(3)]
    public required double SimulationTime { get; init; }

    /// <summary>Vehicle speed, in meters per second.</summary>
    [Key(4)]
    public required float Speed { get; init; }

    /// <summary>Throttle input, 0 (released) to 1 (full throttle). <see langword="null"/> if the source does not report it.</summary>
    [Key(5)]
    public float? Throttle { get; init; }

    /// <summary>Brake input, 0 (released) to 1 (full brake). <see langword="null"/> if the source does not report it.</summary>
    [Key(6)]
    public float? Brake { get; init; }

    /// <summary>Steering input, -1 (full left) to 1 (full right).</summary>
    [Key(7)]
    public required float Steering { get; init; }

    /// <summary>Current gear. -2 = not available/unknown, -1 = reverse, 0 = neutral, greater than 0 = forward gear number.</summary>
    [Key(8)]
    public required int Gear { get; init; }

    /// <summary>Engine speed, in revolutions per minute.</summary>
    [Key(9)]
    public required float EngineRpm { get; init; }

    /// <summary>Fuel remaining, in liters.</summary>
    [Key(10)]
    public required float FuelLeft { get; init; }

    /// <summary>Current lap number.</summary>
    [Key(11)]
    public required int LapNumber { get; init; }

    /// <summary>Current track sector.</summary>
    [Key(12)]
    public required int Sector { get; init; }

    /// <summary>Current race position. <see langword="null"/> if not applicable or not reported.</summary>
    [Key(13)]
    public int? Position { get; init; }

    /// <summary>Wheel rotational speed expressed as an equivalent linear speed, in meters per second.</summary>
    [Key(14)]
    public required float WheelSpeedFrontLeft { get; init; }

    /// <inheritdoc cref="WheelSpeedFrontLeft"/>
    [Key(15)]
    public required float WheelSpeedFrontRight { get; init; }

    /// <inheritdoc cref="WheelSpeedFrontLeft"/>
    [Key(16)]
    public required float WheelSpeedRearLeft { get; init; }

    /// <inheritdoc cref="WheelSpeedFrontLeft"/>
    [Key(17)]
    public required float WheelSpeedRearRight { get; init; }

    /// <summary>Suspension travel, in meters.</summary>
    [Key(18)]
    public required float SuspensionTravelFrontLeft { get; init; }

    /// <inheritdoc cref="SuspensionTravelFrontLeft"/>
    [Key(19)]
    public required float SuspensionTravelFrontRight { get; init; }

    /// <inheritdoc cref="SuspensionTravelFrontLeft"/>
    [Key(20)]
    public required float SuspensionTravelRearLeft { get; init; }

    /// <inheritdoc cref="SuspensionTravelFrontLeft"/>
    [Key(21)]
    public required float SuspensionTravelRearRight { get; init; }

    /// <summary>Tyre pressure, in kilopascals. <see langword="null"/> if the source does not report it.</summary>
    [Key(22)]
    public float? TyrePressureFrontLeft { get; init; }

    /// <inheritdoc cref="TyrePressureFrontLeft"/>
    [Key(23)]
    public float? TyrePressureFrontRight { get; init; }

    /// <inheritdoc cref="TyrePressureFrontLeft"/>
    [Key(24)]
    public float? TyrePressureRearLeft { get; init; }

    /// <inheritdoc cref="TyrePressureFrontLeft"/>
    [Key(25)]
    public float? TyrePressureRearRight { get; init; }

    /// <summary>Tyre wear, 0 (new) to 1 (fully worn). <see langword="null"/> if the source does not report it.</summary>
    [Key(26)]
    public float? TyreWearFrontLeft { get; init; }

    /// <inheritdoc cref="TyreWearFrontLeft"/>
    [Key(27)]
    public float? TyreWearFrontRight { get; init; }

    /// <inheritdoc cref="TyreWearFrontLeft"/>
    [Key(28)]
    public float? TyreWearRearLeft { get; init; }

    /// <inheritdoc cref="TyreWearFrontLeft"/>
    [Key(29)]
    public float? TyreWearRearRight { get; init; }

    /// <summary>Per-wheel tyre temperature detail.</summary>
    [Key(30)]
    public required TyreTemperatureDto TyreTemperatureFrontLeft { get; init; }

    /// <inheritdoc cref="TyreTemperatureFrontLeft"/>
    [Key(31)]
    public required TyreTemperatureDto TyreTemperatureFrontRight { get; init; }

    /// <inheritdoc cref="TyreTemperatureFrontLeft"/>
    [Key(32)]
    public required TyreTemperatureDto TyreTemperatureRearLeft { get; init; }

    /// <inheritdoc cref="TyreTemperatureFrontLeft"/>
    [Key(33)]
    public required TyreTemperatureDto TyreTemperatureRearRight { get; init; }

    /// <summary>Fractional position around the track lap, 0 to 1. <see langword="null"/> if not reported.</summary>
    [Key(34)]
    public float? TrackPositionFraction { get; init; }

    /// <summary>
    /// Simulator-specific fields that have no canonical equivalent, as a raw JSON string (e.g.
    /// <c>"{}"</c> when empty). <see cref="System.Text.Json.JsonElement"/> has no native
    /// MessagePack representation, so it is carried as text and re-parsed on the receiving end.
    /// </summary>
    [Key(35)]
    public required string Extras { get; init; }
}

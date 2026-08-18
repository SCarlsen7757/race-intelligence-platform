using System.Text.Json;

namespace RaceIntelligence.Core.Telemetry;

/// <summary>
/// A single canonical telemetry snapshot, produced by translating a simulator's native telemetry
/// into simulator-agnostic, SI-unit fields.
/// </summary>
/// <remarks>
/// <para>
/// Fields that every simulator reliably reports are non-nullable and required. Fields a
/// simulator may not report at all (e.g. tyre pressure on a sim with no such sensor model) are
/// nullable — either directly, or per-wheel via <see cref="WheelData{T}"/> of a nullable type.
/// </para>
/// <para>
/// <b>Sentinel translation is a connector responsibility, not a Core concern:</b> a source API's
/// "not available" sentinel (for example RaceRoom reports <c>-1.0</c> for several fields it
/// doesn't currently support) MUST be translated to <see langword="null"/> by the connector
/// before it reaches a <see cref="TelemetrySample"/>. It must never be coerced to <c>0</c> — doing
/// so would silently corrupt downstream analysis (a fuel level of "unavailable" is not the same
/// as a fuel level of "empty", and a tyre wear of "unavailable" is not the same as "unworn"), and
/// because raw telemetry is stored permanently, such a mistake would be baked into history.
/// </para>
/// </remarks>
public sealed record TelemetrySample
{
    /// <summary>The session this sample belongs to.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>
    /// Monotonically increasing sequence number assigned by the collector, used to detect gaps
    /// and reorder samples independent of wall-clock <see cref="Timestamp"/> jitter.
    /// </summary>
    public required long SequenceNumber { get; init; }

    /// <summary>Wall-clock time the sample was captured.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Simulator-reported in-session time, in seconds, since the session began.</summary>
    public required double SimulationTime { get; init; }

    /// <summary>Vehicle speed, in meters per second.</summary>
    public required float Speed { get; init; }

    /// <summary>Throttle input, 0 (released) to 1 (full throttle). <see langword="null"/> if the source does not report it.</summary>
    public float? Throttle { get; init; }

    /// <summary>Brake input, 0 (released) to 1 (full brake). <see langword="null"/> if the source does not report it.</summary>
    public float? Brake { get; init; }

    /// <summary>Driver-selected ABS level. <see langword="null"/> when unavailable.</summary>
    public int? AbsSetting { get; init; }

    /// <summary>Whether ABS is intervening in this sample. <see langword="null"/> when unavailable.</summary>
    public bool? AbsActive { get; init; }

    /// <summary>Driver-selected traction-control level. <see langword="null"/> when unavailable.</summary>
    public int? TractionControlSetting { get; init; }

    /// <summary>Whether traction control is intervening in this sample. <see langword="null"/> when unavailable.</summary>
    public bool? TractionControlActive { get; init; }

    /// <summary>Brake-bias fraction toward the rear axle. <see langword="null"/> when unavailable.</summary>
    public float? BrakeBias { get; init; }

    /// <summary>
    /// Clutch input, 0 (fully engaged, pedal up) to 1 (fully disengaged, pedal down).
    /// <see langword="null"/> if the source does not report it.
    /// </summary>
    /// <remarks>
    /// Nullable for a reason that bites more often than it does for throttle or brake: a car with an
    /// automatic clutch, or a simulator watching a remote or AI car, reports nothing here at all.
    /// A missing clutch reading is emphatically not a released one — rendered as <c>0</c> it claims
    /// the driver was fully off the clutch through a launch nobody has any data for.
    /// </remarks>
    public float? Clutch { get; init; }

    /// <summary>Steering input, -1 (full left) to 1 (full right).</summary>
    public required float Steering { get; init; }

    /// <summary>
    /// Current gear: -1 = reverse, 0 = neutral, greater than 0 = forward gear number.
    /// <see langword="null"/> if the source does not report it.
    /// </summary>
    public int? Gear { get; init; }

    /// <summary>Engine speed, in revolutions per minute. Explicitly RPM, not radians/second.</summary>
    public required float EngineRpm { get; init; }

    /// <summary>Fuel remaining, in liters.</summary>
    public required float FuelLeft { get; init; }

    /// <summary>Current lap number.</summary>
    public required int LapNumber { get; init; }

    /// <summary>Current track sector.</summary>
    public required int Sector { get; init; }

    /// <summary>Current race position. <see langword="null"/> if not applicable (e.g. session type has no classification) or not reported.</summary>
    public int? Position { get; init; }

    /// <summary>Per-wheel rotational speed expressed as an equivalent linear speed, in meters per second.</summary>
    public required WheelData<float> WheelSpeed { get; init; }

    /// <summary>Per-wheel suspension travel, in meters.</summary>
    public required WheelData<float> SuspensionTravel { get; init; }

    /// <summary>Per-wheel tyre temperature detail.</summary>
    public required WheelData<TyreTemperature> TyreTemperature { get; init; }

    /// <summary>
    /// Per-wheel tyre pressure, in kilopascals. Individual wheels are <see langword="null"/> if
    /// the source does not report tyre pressure.
    /// </summary>
    public WheelData<float?> TyrePressure { get; init; }

    /// <summary>
    /// Per-wheel tyre wear, 0 (new) to 1 (fully worn). Individual wheels are
    /// <see langword="null"/> if the source does not report tyre wear.
    /// </summary>
    public WheelData<float?> TyreWear { get; init; }

    /// <summary>Fractional position around the track lap, 0 to 1. <see langword="null"/> if not reported.</summary>
    public float? TrackPositionFraction { get; init; }

    /// <summary>
    /// Simulator-specific fields that have no canonical equivalent (e.g. push-to-pass state, ERS
    /// mode), as raw JSON text, so new simulator fields never require a schema or model change
    /// here; see the platform's "Canonical Fields + Flexible Metadata" design.
    /// </summary>
    /// <remarks>
    /// Text rather than a <see cref="JsonElement"/> because nothing on the path from connector to
    /// <c>jsonb</c> column reads inside it: the connector writes it, the wire carries it as a
    /// string, and Postgres parses it. Holding it as a <see cref="JsonElement"/> forced a
    /// parse-and-clone on the way in and a re-serialize on the way out, twice each, for every
    /// sample at the poll rate. Producers are responsible for writing valid JSON; the ingest API
    /// validates it on arrival, since there it is untrusted client input.
    /// </remarks>
    public required string Extras { get; init; }
}

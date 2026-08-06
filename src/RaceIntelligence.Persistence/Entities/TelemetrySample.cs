using System.Text.Json;

namespace RaceIntelligence.Persistence.Entities;

/// <summary>
/// A single high-frequency telemetry snapshot. Persisted form of
/// <see cref="RaceIntelligence.Core.Telemetry.TelemetrySample"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raw telemetry is immutable and permanent</b> — this table is insert-only. There are
/// deliberately no update/delete helpers anywhere in this project for this entity. The running
/// system's only write path is <c>Bulk/NpgsqlTelemetryWriter</c>, and even that only ever inserts;
/// this entity itself is written through EF only by tests, which use it to check that the bulk
/// path and the mapped model agree.
/// </para>
/// <para>
/// <b>Why the primary key is <c>(session_id, timestamp, sequence_number)</c> and not a surrogate
/// id:</b>
/// </para>
/// <list type="bullet">
/// <item>No surrogate key saves 16 bytes/row on by far the largest table in the schema.</item>
/// <item>
/// <c>session_id</c> leading makes "all telemetry for session X in order" a pure primary-key
/// range scan — the single most common query pattern (loading one session for analysis).
/// </item>
/// <item>
/// <c>timestamp</c> is part of the key specifically because TimescaleDB requires the
/// hypertable's partitioning column to be part of every unique/primary-key constraint on that
/// table. Including it now means <c>create_hypertable('telemetry_samples', 'timestamp')</c> later
/// is a one-line optimization, not a schema redesign — exactly what the platform's "can migrate to
/// TimescaleDB later" goal requires.
/// </item>
/// <item>
/// <c>sequence_number</c> is a collector-assigned, monotonically increasing-per-session counter.
/// Its role in the key is what makes a retried upload batch idempotent: re-uploading the same
/// batch produces the same key values, so <c>ON CONFLICT DO NOTHING</c> silently drops the
/// duplicates instead of double-counting telemetry.
/// </item>
/// </list>
/// <para>
/// Enum-shaped fields (e.g. <see cref="RaceIntelligence.Core.Sessions.SessionType"/> on
/// <see cref="Session"/>) are stored as <c>smallint</c> rather than native Postgres enum types,
/// because <c>ALTER TYPE ... ADD VALUE</c> has transactional restrictions (it cannot run inside
/// the same transaction as other DDL/DML in older Postgres, and even on versions that relax this,
/// it fights straightforward rolling deploys) that conflict with this platform's "connectors and
/// algorithms are replaceable" philosophy — adding a new session type or capability should never
/// require special-cased migration ceremony.
/// </para>
/// </remarks>
public sealed class TelemetrySample
{
    /// <summary>The session this sample belongs to. Part of the composite primary key.</summary>
    public Guid SessionId { get; set; }

    public Session? Session { get; set; }

    /// <summary>Wall-clock time the sample was captured. Part of the composite primary key (and the future TimescaleDB partitioning column).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Collector-assigned, monotonically increasing (per session) sequence number. Part of the
    /// composite primary key; makes retried upload batches idempotent via
    /// <c>ON CONFLICT DO NOTHING</c>.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>Simulator-reported in-session time, in seconds, since the session began.</summary>
    public double SimulationTime { get; set; }

    /// <summary>Current lap number.</summary>
    public int LapNumber { get; set; }

    /// <summary>Current track sector.</summary>
    public int Sector { get; set; }

    /// <summary>Vehicle speed, in meters per second.</summary>
    public float Speed { get; set; }

    /// <summary>Throttle input, 0 (released) to 1 (full throttle). <see langword="null"/> if the source does not report it.</summary>
    public float? Throttle { get; set; }

    /// <summary>Brake input, 0 (released) to 1 (full brake). <see langword="null"/> if the source does not report it.</summary>
    public float? Brake { get; set; }

    /// <summary>Steering input, -1 (full left) to 1 (full right).</summary>
    public float Steering { get; set; }

    /// <summary>Current gear, stored as <c>smallint</c>: -1 = reverse, 0 = neutral, greater than 0 = forward gear number. <see langword="null"/> if the source does not report it.</summary>
    public short? Gear { get; set; }

    /// <summary>Engine speed, in revolutions per minute.</summary>
    public float EngineRpm { get; set; }

    /// <summary>Fuel remaining, in liters.</summary>
    public float FuelLeft { get; set; }

    /// <summary>Current race position, stored as <c>smallint</c>. <see langword="null"/> if not applicable or not reported.</summary>
    public short? Position { get; set; }

    /// <summary>Fractional position around the track lap, 0 to 1. <see langword="null"/> if not reported.</summary>
    public float? TrackPositionFraction { get; set; }

    /// <summary>Per-wheel rotational speed expressed as an equivalent linear speed, in meters per second, ordered [FL, FR, RL, RR].</summary>
    public required float[] WheelSpeed { get; set; }

    /// <summary>Per-wheel suspension travel, in meters, ordered [FL, FR, RL, RR].</summary>
    public required float[] SuspensionTravel { get; set; }

    /// <summary>
    /// Per-wheel tyre pressure, in kilopascals, ordered [FL, FR, RL, RR]. The whole column is
    /// <see langword="null"/> when the source does not report tyre pressure at all; individual
    /// array elements may still be <see langword="null"/> for a single wheel.
    /// </summary>
    public float?[]? TyrePressure { get; set; }

    /// <summary>
    /// Per-wheel tyre wear, 0 (new) to 1 (fully worn), ordered [FL, FR, RL, RR]. The whole column
    /// is <see langword="null"/> when the source does not report tyre wear at all; individual
    /// array elements may still be <see langword="null"/> for a single wheel.
    /// </summary>
    public float?[]? TyreWear { get; set; }

    /// <summary>Per-wheel tyre temperature detail, stored as jsonb (see <c>Mapping/TelemetrySampleMapper</c> for the shape).</summary>
    public JsonElement TyreTemperature { get; set; }

    /// <summary>Simulator-specific fields that have no canonical equivalent, as raw JSON text in a jsonb column.</summary>
    /// <remarks>Text, not a <see cref="JsonElement"/>: nothing here reads inside it, and Npgsql sends a string to jsonb directly.</remarks>
    public string Extras { get; set; } = null!;
}

using RaceIntelligence.RaceRoom.Channels;

namespace RaceIntelligence.Persistence.RaceRoom.Entities;

/// <summary>
/// One row of <c>telemetry_samples</c> — every RaceRoom channel as its own typed column.
/// </summary>
/// <remarks>
/// <para>
/// <b>This lives here rather than in Persistence.Core because the sample is RaceRoom's.</b> Storage
/// is per-simulator (ADR 0001) and, since #109, so is the sample: a hundred and seventy-five columns
/// naming push-to-pass, DRS, third-spring velocity and RaceRoom's own surface-material codes are not
/// a canonical model that a second simulator would inherit. Core keeps sessions, laps, drivers,
/// tracks and cars, which genuinely are shared.
/// </para>
/// <para>
/// <b>The columns are generated</b> from <c>channels/raceroom-telemetry.channels</c>, and so are
/// <c>Columns</c>, <c>CopyRowAsync</c> and <c>ConfigureChannels</c>. That is not tidiness. A binary
/// <c>COPY</c> takes a column list and a stream of positional values and checks neither against the
/// other: a list and a writer that disagree put camber into ride height and report success. Deriving
/// all of them from one loop over one list makes the mismatch unexpressible.
/// </para>
/// <para>
/// <b>The primary key is <c>(session_id, timestamp, sequence_number)</c>.</b> Timestamp is in it so
/// the table can become a TimescaleDB hypertable without a key change — the partitioning column has
/// to be part of every unique index — and <c>sequence_number</c> is in it because it is what makes a
/// retried upload batch idempotent: it is collector-assigned and monotonic per session, so the same
/// sample re-sent produces the same key and <c>ON CONFLICT DO NOTHING</c> skips it.
/// </para>
/// <para>
/// <b>Insert-only.</b> The one runtime write path is <c>NpgsqlTelemetryWriter</c>; EF writes here
/// only in tests. Nothing updates or deletes a sample.
/// </para>
/// <para>
/// <b>Absent is not zero.</b> A nullable column means the simulator did not report a value. RaceRoom
/// signals that with <c>-1</c>, and the connector has already turned it into <see langword="null"/>
/// before a sample reaches this type (ADR 0002 section 3) — there is no sentinel left in the
/// database, which is what makes <c>AVG(tyre_grip_fl)</c> a question you can ask.
/// </para>
/// </remarks>
[GeneratedFromChannels(
    ChannelArtifact.Entity,
    Companion = "RaceIntelligence.RaceRoom.Telemetry.RaceRoomTelemetrySample")]
public sealed partial class TelemetrySample;

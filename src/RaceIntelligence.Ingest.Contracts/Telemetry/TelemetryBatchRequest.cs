using MessagePack;
using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Ingest.Contracts.Telemetry;

/// <summary>
/// Request body for <c>POST /api/v1/sessions/{id}/telemetry:batch</c>: a batch of raw telemetry
/// samples, serialized with MessagePack for the 50-60 Hz hot path.
/// </summary>
/// <remarks>
/// Duplicate detection happens per-row at the database primary key
/// <c>(session_id, timestamp, sequence_number)</c> — see <c>NpgsqlTelemetryWriter</c> — so a batch
/// may be re-submitted verbatim after a network failure with no risk of double-counting.
/// </remarks>
/// <param name="SchemaVersion">The wire schema version this body was written against. See <see cref="Contracts.SchemaVersion"/>.</param>
/// <param name="SessionId">The session every sample in <paramref name="Samples"/> belongs to.</param>
/// <param name="FirstSequenceNumber">
/// The lowest <see cref="RaceRoomTelemetrySample.SequenceNumber"/> in this batch. Carried for the
/// collector's own upload logging and for operators reading a captured request; the server reads
/// neither this nor <paramref name="LastSequenceNumber"/> — it works from the samples themselves,
/// so a batch whose declared range disagrees with its contents is still stored correctly.
/// </param>
/// <param name="LastSequenceNumber">The highest <see cref="RaceRoomTelemetrySample.SequenceNumber"/> in this batch. See <paramref name="FirstSequenceNumber"/>.</param>
/// <param name="Samples">The telemetry samples in this batch. May be empty.</param>
/// <param name="OperatingWindows">
/// The tyre and brake temperature bands in force for this session, one row per corner and compound.
/// Constant for a compound, so the same rows ride on every batch and the server keeps the first of
/// each — see <see cref="OperatingWindow"/>. May be empty when the simulator reported none.
/// </param>
[MessagePackObject]
public sealed record TelemetryBatchRequest(
    [property: Key(0)] int SchemaVersion,
    [property: Key(1)] Guid SessionId,
    [property: Key(2)] long FirstSequenceNumber,
    [property: Key(3)] long LastSequenceNumber,
    [property: Key(4)] IReadOnlyList<RaceRoomTelemetrySample> Samples,
    [property: Key(5)] IReadOnlyList<OperatingWindow> OperatingWindows);

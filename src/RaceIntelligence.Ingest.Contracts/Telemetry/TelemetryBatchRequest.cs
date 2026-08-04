using MessagePack;

namespace RaceIntelligence.Ingest.Contracts.Telemetry;

/// <summary>
/// Request body for <c>POST /api/v1/sessions/{id}/telemetry:batch</c>: a batch of raw telemetry
/// samples, serialized with MessagePack for the 50-60 Hz hot path.
/// </summary>
/// <remarks>
/// <see cref="FirstSequenceNumber"/>/<see cref="LastSequenceNumber"/> let the server (and the
/// collector's own retry bookkeeping) reason about batch coverage without decoding every sample
/// first. Actual duplicate detection happens per-row at the database primary key
/// <c>(session_id, timestamp, sequence_number)</c> — see <c>ITelemetryWriter</c> — so a batch may
/// be re-submitted verbatim after a network failure with no risk of double-counting.
/// </remarks>
/// <param name="SchemaVersion">The wire schema version this body was written against. See <see cref="Contracts.SchemaVersion"/>.</param>
/// <param name="SessionId">The session every sample in <paramref name="Samples"/> belongs to.</param>
/// <param name="FirstSequenceNumber">The lowest <see cref="TelemetrySampleDto.SequenceNumber"/> in this batch.</param>
/// <param name="LastSequenceNumber">The highest <see cref="TelemetrySampleDto.SequenceNumber"/> in this batch.</param>
/// <param name="Samples">The telemetry samples in this batch. May be empty.</param>
[MessagePackObject]
public sealed record TelemetryBatchRequest(
    [property: Key(0)] int SchemaVersion,
    [property: Key(1)] Guid SessionId,
    [property: Key(2)] long FirstSequenceNumber,
    [property: Key(3)] long LastSequenceNumber,
    [property: Key(4)] IReadOnlyList<TelemetrySampleDto> Samples);

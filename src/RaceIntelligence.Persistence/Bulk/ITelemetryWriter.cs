using CoreTelemetry = RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Persistence.Bulk;

/// <summary>Bulk, insert-only write path for raw telemetry. See <see cref="NpgsqlTelemetryWriter"/> for the implementation.</summary>
public interface ITelemetryWriter
{
    /// <summary>
    /// Writes a batch of telemetry samples for <paramref name="sessionId"/>, idempotently.
    /// Re-submitting a batch (or a batch overlapping one already written, e.g. after a retried
    /// upload) never creates duplicate rows: samples whose primary key
    /// <c>(session_id, timestamp, sequence_number)</c> already exists are silently skipped and
    /// counted as <see cref="TelemetryWriteResult.Duplicates"/>.
    /// </summary>
    /// <param name="sessionId">
    /// The session every sample in <paramref name="samples"/> is expected to belong to. Used to
    /// validate the batch; every sample's own <c>SessionId</c> must match.
    /// </param>
    /// <param name="samples">The samples to write. May be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TelemetryWriteResult> WriteAsync(Guid sessionId, IReadOnlyList<CoreTelemetry.TelemetrySample> samples, CancellationToken ct = default);
}

/// <summary>The outcome of a bulk telemetry write.</summary>
/// <param name="Inserted">Number of samples actually inserted as new rows.</param>
/// <param name="Duplicates">Number of samples skipped because their primary key already existed.</param>
public sealed record TelemetryWriteResult(int Inserted, int Duplicates);

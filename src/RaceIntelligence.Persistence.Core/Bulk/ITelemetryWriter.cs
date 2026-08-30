using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Persistence.Core.Bulk;

/// <summary>
/// Writes raw telemetry into one simulator's store.
/// </summary>
/// <remarks>
/// <para>
/// An interface because the write path is per-simulator: each store owns its own tables, so each
/// owns its own <c>COPY</c> — the column list and its order are literal in the implementation, and
/// a simulator with its own first-class channels has its own columns to write. The ingest endpoint
/// is the same code for every simulator and only needs "put these samples somewhere".
/// </para>
/// <para>
/// <b>Implementations must be idempotent.</b> A collector retries an upload it did not get an
/// answer to, so a batch that overlaps one already written must skip the rows that exist rather
/// than duplicating them, and report how many it skipped. Raw telemetry is insert-only: nothing
/// implementing this may update or delete a sample.
/// </para>
/// </remarks>
public interface ITelemetryWriter
{
    /// <summary>
    /// Writes a batch of samples for one session, skipping any whose key is already stored.
    /// </summary>
    /// <param name="sessionId">The session every sample is expected to belong to.</param>
    /// <param name="samples">The batch, in any order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many rows were inserted, and how many were already there.</returns>
    Task<TelemetryWriteResult> WriteAsync(
        Guid sessionId,
        IReadOnlyList<RaceRoomTelemetrySample> samples,
        CancellationToken ct = default);
}

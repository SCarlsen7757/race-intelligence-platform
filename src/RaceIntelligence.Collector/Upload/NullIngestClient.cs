using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Telemetry;

namespace RaceIntelligence.Collector.Upload;

/// <summary>
/// The ingest client used when archiving is switched off: every call succeeds and does nothing.
/// </summary>
/// <remarks>
/// A publish-only collector runs the same collect loop as any other, and that loop records sessions
/// and laps through <see cref="IIngestClient"/>. Rather than guard each of those calls with a check
/// nobody will remember to add to the fourth one, the decision is made once at registration and the
/// loop keeps a single code path.
/// <para>
/// Reports every batch as fully accepted. The alternative — reporting zero accepted — would make
/// <see cref="TelemetryUploadService"/> log a discrepancy on every flush for a collector that is
/// behaving exactly as configured.
/// </para>
/// </remarks>
public sealed class NullIngestClient : IIngestClient
{
    /// <inheritdoc />
    public Task CreateSessionAsync(SessionCreateRequest request, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task UpdateSessionAsync(Guid sessionId, SessionUpdateRequest request, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task RecordLapAsync(Guid sessionId, LapCompletedRequest request, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<TelemetryBatchResponse> UploadTelemetryBatchAsync(
        Guid sessionId,
        TelemetryBatchRequest batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return Task.FromResult(new TelemetryBatchResponse(
            batch.Samples.Count,
            Duplicates: 0,
            ServerReceivedAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }
}

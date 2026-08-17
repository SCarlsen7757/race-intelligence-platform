using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Telemetry;

namespace RaceIntelligence.Collector.Plugins.Ingest.Upload;

/// <summary>
/// Typed client over the ingest API's session, lap, and telemetry-batch endpoints — the collector's
/// only channel to the outside world. The collector holds no database credentials; every write it
/// makes goes through this interface and, in turn, plain HTTP.
/// </summary>
public interface IIngestClient
{
    /// <summary>
    /// Registers a new session with the ingest API (<c>POST /api/v1/sessions</c>). Idempotent on
    /// <see cref="SessionCreateRequest.SessionId"/> — safe to retry.
    /// </summary>
    Task CreateSessionAsync(SessionCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a partial update to an existing session (<c>PATCH /api/v1/sessions/{id}</c>), most
    /// notably setting <see cref="SessionUpdateRequest.EndedAtUtc"/> once the session finishes.
    /// </summary>
    Task UpdateSessionAsync(Guid sessionId, SessionUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records (or upserts) a completed lap's summary statistics (<c>POST /api/v1/sessions/{id}/laps</c>).
    /// </summary>
    Task RecordLapAsync(Guid sessionId, LapCompletedRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a batch of raw telemetry samples (<c>POST /api/v1/sessions/{id}/telemetry:batch</c>),
    /// MessagePack-encoded for the hot path.
    /// </summary>
    Task<TelemetryBatchResponse> UploadTelemetryBatchAsync(Guid sessionId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default);
}

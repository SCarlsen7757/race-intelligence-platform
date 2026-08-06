using RaceIntelligence.Collector.Upload;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Telemetry;

namespace RaceIntelligence.Collector.Tests.Support;

/// <summary>
/// Fake <see cref="IIngestClient"/> that records every call, optionally appending a marker to a
/// shared, ordered log so a test can assert cross-component call ordering (e.g. a session create
/// happening before the samples it covers are buffered).
/// </summary>
internal sealed class RecordingIngestClient(List<string>? sharedLog = null) : IIngestClient
{
    private readonly List<string> _log = sharedLog ?? [];

    public List<SessionCreateRequest> CreatedSessions { get; } = [];

    public List<(Guid SessionId, SessionUpdateRequest Request)> UpdatedSessions { get; } = [];

    public List<(Guid SessionId, LapCompletedRequest Request)> RecordedLaps { get; } = [];

    public List<TelemetryBatchRequest> UploadedBatches { get; } = [];

    /// <summary>
    /// When set, every <see cref="UploadTelemetryBatchAsync"/> call throws this instead of
    /// succeeding — the shape of a failure that has already exhausted the HttpClient's resilience
    /// policy. Without it there was no way to reach <see cref="TelemetryUploadService"/>'s
    /// catch-log-and-discard path, which is the one place the pipeline knowingly loses data.
    /// </summary>
    public Exception? FailUploadsWith { get; set; }

    /// <summary>Batches that were attempted while <see cref="FailUploadsWith"/> was set, in order.</summary>
    public List<TelemetryBatchRequest> FailedUploadAttempts { get; } = [];

    public Task CreateSessionAsync(SessionCreateRequest request, CancellationToken cancellationToken = default)
    {
        lock (_log)
        {
            _log.Add($"CreateSession:{request.SessionId}");
        }

        CreatedSessions.Add(request);
        return Task.CompletedTask;
    }

    public Task UpdateSessionAsync(Guid sessionId, SessionUpdateRequest request, CancellationToken cancellationToken = default)
    {
        lock (_log)
        {
            _log.Add($"UpdateSession:{sessionId}");
        }

        UpdatedSessions.Add((sessionId, request));
        return Task.CompletedTask;
    }

    public Task RecordLapAsync(Guid sessionId, LapCompletedRequest request, CancellationToken cancellationToken = default)
    {
        lock (_log)
        {
            _log.Add($"RecordLap:{sessionId}:{request.LapNumber}");
        }

        RecordedLaps.Add((sessionId, request));
        return Task.CompletedTask;
    }

    public Task<TelemetryBatchResponse> UploadTelemetryBatchAsync(Guid sessionId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default)
    {
        if (FailUploadsWith is { } failure)
        {
            lock (_log)
            {
                _log.Add($"UploadBatchFailed:{sessionId}:{batch.Samples.Count}");
            }

            FailedUploadAttempts.Add(batch);
            return Task.FromException<TelemetryBatchResponse>(failure);
        }

        lock (_log)
        {
            _log.Add($"UploadBatch:{sessionId}:{batch.Samples.Count}");
        }

        UploadedBatches.Add(batch);
        return Task.FromResult(new TelemetryBatchResponse(batch.Samples.Count, 0, 0));
    }
}

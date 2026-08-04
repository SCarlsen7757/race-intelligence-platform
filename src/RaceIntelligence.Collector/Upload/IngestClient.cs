using System.Net.Http.Headers;
using System.Net.Http.Json;
using MessagePack;
using Microsoft.Extensions.Logging;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Telemetry;

namespace RaceIntelligence.Collector.Upload;

/// <summary>
/// <see cref="IIngestClient"/> implementation over a typed <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class only knows how to shape and send requests — it deliberately has no knowledge of
/// <c>CollectorOptions</c>. The <see cref="HttpClient"/> it receives is expected to already carry
/// the ingest API's <see cref="HttpClient.BaseAddress"/> and the <c>X-Api-Key</c> header, both set
/// once at registration time in <c>Program.cs</c> via <c>AddHttpClient&lt;IIngestClient, IngestClient&gt;</c>
/// — keeping that configuration in one place also means it never drifts between requests.
/// </para>
/// <para>
/// Retries, timeouts, and circuit-breaking are handled entirely by the resilience handler attached
/// to the <see cref="HttpClient"/> at registration (<c>AddStandardResilienceHandler</c>); this class
/// does not hand-roll any of its own. A response that is still unsuccessful after resilience has
/// exhausted its policy is surfaced as a thrown <see cref="HttpRequestException"/> rather than
/// swallowed, so callers (<c>TelemetryCollectorService</c>, <c>TelemetryUploadService</c>) can log
/// and account for it instead of silently believing an upload succeeded.
/// </para>
/// </remarks>
public sealed class IngestClient(HttpClient httpClient, ILogger<IngestClient> logger) : IIngestClient
{
    private static readonly MediaTypeHeaderValue MessagePackMediaType = new("application/x-msgpack");

    /// <inheritdoc />
    public async Task CreateSessionAsync(SessionCreateRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/sessions", request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"create session {request.SessionId}", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateSessionAsync(Guid sessionId, SessionUpdateRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PatchAsJsonAsync($"api/v1/sessions/{sessionId}", request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"update session {sessionId}", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordLapAsync(Guid sessionId, LapCompletedRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/v1/sessions/{sessionId}/laps", request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"record lap {request.LapNumber} for session {sessionId}", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TelemetryBatchResponse> UploadTelemetryBatchAsync(Guid sessionId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default)
    {
        byte[] payload = MessagePackSerializer.Serialize(batch, TelemetryMessagePackOptions.Default, cancellationToken);
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = MessagePackMediaType;

        using var response = await httpClient.PostAsync($"api/v1/sessions/{sessionId}/telemetry:batch", content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"upload telemetry batch ({batch.Samples.Count} samples) for session {sessionId}", cancellationToken).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<TelemetryBatchResponse>(cancellationToken).ConfigureAwait(false);
        return result ?? throw new HttpRequestException(
            $"Ingest API returned an empty response body for a telemetry batch upload for session {sessionId}.");
    }

    /// <summary>
    /// Throws a descriptive <see cref="HttpRequestException"/> for a non-success response, after
    /// logging the response body for diagnostics — a failed upload must be visible, never silently
    /// treated as delivered.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        logger.LogError(
            "Ingest API request to {Action} failed with status {StatusCode}: {Detail}",
            action, (int)response.StatusCode, detail);

        throw new HttpRequestException(
            $"Ingest API request to {action} failed with status {(int)response.StatusCode}: {detail}",
            inner: null,
            response.StatusCode);
    }
}

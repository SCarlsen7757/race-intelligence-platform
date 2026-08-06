using System.Net.Http.Headers;
using System.Net.Http.Json;
using MessagePack;
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
/// to the <see cref="HttpClient"/> at registration (<c>AddStandardResilienceHandler</c>, applied to
/// every client by <c>AddServiceDefaults</c>); this class does not hand-roll any of its own. A
/// response that is still unsuccessful after resilience has exhausted its policy is surfaced as a
/// thrown <see cref="HttpRequestException"/> rather than swallowed, so callers
/// (<c>TelemetryCollectorService</c>, <c>TelemetryUploadService</c>) can log and account for it
/// instead of silently believing an upload succeeded.
/// </para>
/// <para>
/// This class deliberately does <b>no logging of its own.</b> The failure detail travels in the
/// thrown exception, and each caller already logs that exception once, at the severity appropriate
/// to what it means there. Logging here as well simply reported the same failure two to four times
/// — with the full response body each time.
/// </para>
/// </remarks>
public sealed class IngestClient(HttpClient httpClient) : IIngestClient
{
    /// <summary>
    /// Cap on how much of an error response body is carried in the exception message. A failing
    /// ingest API can answer with an arbitrarily large body, and the whole thing would otherwise
    /// end up in the log line for every failed batch.
    /// </summary>
    private const int MaxDetailLength = 2_000;

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
    /// Throws a descriptive <see cref="HttpRequestException"/> for a non-success response, carrying
    /// the status and (truncated) response body — a failed upload must be visible, never silently
    /// treated as delivered. Does not log: the caller that catches this logs it once.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detail.Length > MaxDetailLength)
        {
            detail = string.Concat(detail.AsSpan(0, MaxDetailLength), "… (truncated)");
        }

        throw new HttpRequestException(
            $"Ingest API request to {action} failed with status {(int)response.StatusCode}: {detail}",
            inner: null,
            response.StatusCode);
    }
}

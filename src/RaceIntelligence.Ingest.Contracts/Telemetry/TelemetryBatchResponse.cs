namespace RaceIntelligence.Ingest.Contracts.Telemetry;

/// <summary>
/// Response body for <c>POST /api/v1/sessions/{id}/telemetry:batch</c>. Sent as plain JSON: unlike
/// the request, this is a small, low-frequency payload (one per batch upload, not one per sample).
/// </summary>
/// <param name="Accepted">Number of samples actually inserted as new rows.</param>
/// <param name="Duplicates">Number of samples skipped because they had already been written by a previous attempt at this batch.</param>
/// <param name="ServerReceivedAtUnixMs">Server wall-clock time the batch was processed, as Unix milliseconds, for client-side clock-skew/latency diagnostics.</param>
public sealed record TelemetryBatchResponse(int Accepted, int Duplicates, long ServerReceivedAtUnixMs);

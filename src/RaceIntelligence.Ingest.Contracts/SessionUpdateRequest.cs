namespace RaceIntelligence.Ingest.Contracts;

/// <summary>
/// Request body for <c>PATCH /api/v1/sessions/{id}</c>: records information only known once a
/// session has progressed or ended (end time, weather, setup).
/// </summary>
/// <remarks>
/// Every field besides <see cref="SchemaVersion"/> is optional and independently applied:
/// <see langword="null"/> means "leave unchanged," not "clear this field." This lets the collector
/// send a weather update mid-session without needing to already know the eventual end time, and
/// vice versa.
/// </remarks>
/// <param name="SchemaVersion">The wire schema version this body was written against. See <see cref="SchemaVersion"/>.</param>
/// <param name="EndedAtUtc">UTC time the session ended, if it has ended. <see langword="null"/> to leave unchanged.</param>
/// <param name="WeatherJson">Weather conditions, as a raw JSON string. <see langword="null"/> to leave unchanged.</param>
/// <param name="SetupJson">Car setup used, as a raw JSON string. <see langword="null"/> to leave unchanged.</param>
/// <param name="ExtrasJson">Simulator-specific session metadata with no canonical equivalent, as a raw JSON string. <see langword="null"/> to leave unchanged.</param>
public sealed record SessionUpdateRequest(
    int SchemaVersion,
    DateTimeOffset? EndedAtUtc,
    string? WeatherJson,
    string? SetupJson,
    string? ExtrasJson);

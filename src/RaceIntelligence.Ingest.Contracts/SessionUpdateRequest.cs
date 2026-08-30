namespace RaceIntelligence.Ingest.Contracts;

/// <summary>
/// Request body for <c>PATCH /api/v1/sessions/{id}</c>: records information only known once a
/// session has progressed or ended.
/// </summary>
/// <remarks>
/// <para>
/// Every field besides <see cref="SchemaVersion"/> is optional and independently applied:
/// <see langword="null"/> means "leave unchanged," not "clear this field."
/// </para>
/// <para>
/// This used to carry weather and setup as well. Neither was ever sent: RaceRoom has no dynamic
/// weather and exports none, and it has no readable setup export a connector could persist, so both
/// columns were NULL on every row of every session and always would have been (#109).
/// </para>
/// </remarks>
/// <param name="SchemaVersion">The wire schema version this body was written against. See <see cref="SchemaVersion"/>.</param>
/// <param name="EndedAtUtc">UTC time the session ended, if it has ended. <see langword="null"/> to leave unchanged.</param>
/// <param name="ExtrasJson">Simulator-specific session metadata with no canonical equivalent, as a raw JSON string. <see langword="null"/> to leave unchanged.</param>
public sealed record SessionUpdateRequest(
    int SchemaVersion,
    DateTimeOffset? EndedAtUtc,
    string? ExtrasJson);

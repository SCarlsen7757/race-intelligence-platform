namespace RaceIntelligence.Ingest.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/sessions</c>: registers a new telemetry-collection session, or
/// confirms an already-registered one.
/// </summary>
/// <remarks>
/// Idempotent on <see cref="SessionId"/>: the collector assigns the session id itself (it must be
/// known before the first telemetry sample can reference it), so a retried create after a dropped
/// response must return success rather than a duplicate-key error. See the API's session endpoint
/// for how that idempotency is implemented.
/// </remarks>
/// <param name="SchemaVersion">The wire schema version this body was written against. See <see cref="SchemaVersion"/>.</param>
/// <param name="SessionId">Unique identifier for this session, assigned by the collector. Also referenced by every lap and telemetry sample the session produces.</param>
/// <param name="GameVersion">The exact simulator build, telemetry API version, and connector version that will produce this session's data.</param>
/// <param name="Capabilities">The <see cref="RaceIntelligence.Core.Capabilities.SimCapabilities"/> flag set available from this session's source, as its raw bit pattern.</param>
/// <param name="TrackName">Name of the track.</param>
/// <param name="LayoutName">Name of the specific layout/configuration of the track used.</param>
/// <param name="LayoutLengthMeters">Length of the layout, in meters, if known.</param>
/// <param name="SessionType">The <see cref="RaceIntelligence.Core.Sessions.SessionType"/> value describing the kind of session.</param>
/// <param name="StartedAtUtc">UTC time the session started.</param>
/// <param name="PlayerName">Name of the player/driver, if known.</param>
/// <param name="CarName">Name of the car driven, if known.</param>
/// <param name="CarClassName">Name of the car's class, if known.</param>
/// <param name="ManufacturerName">Name of the car's manufacturer, if known.</param>
/// <param name="ExtrasJson">Simulator-specific session metadata with no canonical equivalent, as a raw JSON string. <see langword="null"/> if none.</param>
public sealed record SessionCreateRequest(
    int SchemaVersion,
    Guid SessionId,
    GameVersionDto GameVersion,
    ulong Capabilities,
    string TrackName,
    string LayoutName,
    float? LayoutLengthMeters,
    int SessionType,
    DateTimeOffset StartedAtUtc,
    string? PlayerName,
    string? CarName,
    string? CarClassName,
    string? ManufacturerName,
    string? ExtrasJson);

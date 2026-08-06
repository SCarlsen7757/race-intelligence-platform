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
/// <param name="SessionType">The kind of session, as the sim's raw session-type value reinterpreted through <see cref="RaceIntelligence.Core.Sessions.SessionType"/>'s underlying type — collectors don't translate it to the enum's named values, since the correct mapping depends on which sim produced it.</param>
/// <param name="StartedAtUtc">UTC time the session started.</param>
/// <param name="PlayerName">Name of the player/driver, if known.</param>
/// <param name="CarName">Name of the car driven, if known.</param>
/// <param name="CarClassName">Name of the car's class, if known.</param>
/// <param name="ManufacturerName">Name of the car's manufacturer, if known.</param>
/// <param name="SimCarId">The sim's own internal identifier for the car driven, if known — populated even when <paramref name="CarName"/> isn't, for sims that expose only numeric ids.</param>
/// <param name="SimCarClassId">The sim's own internal identifier for the car's class, if known. See <paramref name="SimCarId"/>.</param>
/// <param name="SimManufacturerId">The sim's own internal identifier for the car's manufacturer, if known. See <paramref name="SimCarId"/>.</param>
/// <param name="ExtrasJson">Simulator-specific session metadata with no canonical equivalent, as a raw JSON string. <see langword="null"/> if none.</param>
/// <param name="SimDriverId">The sim's own stable identifier for the player, if known — a durable account id rather than a display name, so a session still resolves to the right driver after they rename themselves. Only unique within the sim that issued it, so consumers must scope it by <paramref name="GameVersion"/>'s game. See <paramref name="SimCarId"/> for the same convention applied to cars.</param>
/// <param name="FuelUsageRate">How fast this session consumed fuel relative to real time, as the sim's raw rate code carried through untranslated — like <paramref name="SessionType"/>, collectors don't normalize it to a multiplier, since the encoding is sim-specific (RaceRoom uses <c>-1</c> = not available, <c>0</c> = consumption off, <c>1</c>–<c>4</c> = 1x–4x). <see langword="null"/> only when the source has no such concept.</param>
/// <param name="TyreWearRate">How fast this session wore tyres relative to real time, as the sim's raw rate code carried through untranslated. Same encoding and reasoning as <paramref name="FuelUsageRate"/>, and independent of it — sims let the two be configured separately.</param>
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
    string? ExtrasJson,
    string? SimCarId = null,
    string? SimCarClassId = null,
    string? SimManufacturerId = null,
    string? SimDriverId = null,
    int? FuelUsageRate = null,
    int? TyreWearRate = null);

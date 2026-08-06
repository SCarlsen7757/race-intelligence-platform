using System.Text.Json;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;

namespace RaceIntelligence.Core.Sessions;

/// <summary>
/// Describes a single telemetry-collection session: the car, track, simulator build, and
/// capabilities that were in effect for every <see cref="Telemetry.TelemetrySample"/> and
/// <see cref="LapInfo"/> that references it.
/// </summary>
public sealed record SessionInfo
{
    /// <summary>Unique identifier for this session. Referenced by every sample and lap it produced.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>
    /// The exact game build, telemetry API version, and connector version that produced this
    /// session's data. See <see cref="GameVersionIdentity"/> for why this is recorded per session.
    /// </summary>
    public required GameVersionIdentity GameVersion { get; init; }

    /// <summary>
    /// The capabilities available from this session's source. Analysis and strategy algorithms
    /// must check this rather than <see cref="GameVersion"/> before using a field.
    /// </summary>
    public required SimCapabilities Capabilities { get; init; }

    /// <summary>Name of the track.</summary>
    public required string TrackName { get; init; }

    /// <summary>Name of the specific layout/configuration of the track used.</summary>
    public required string LayoutName { get; init; }

    /// <summary>Length of the layout, in meters, if known.</summary>
    public float? LayoutLengthMeters { get; init; }

    /// <summary>
    /// The type of session. Collectors that perform no analysis (the norm) carry the sim's raw
    /// session-type value through unmapped, reinterpreted through this enum's underlying type
    /// rather than translated to its named values — the correct mapping depends on which sim
    /// produced it, which is a property of the analysis layer, not the collector. A later analysis
    /// pass (which does know the sim) is expected to rewrite this to the canonical numbering.
    /// </summary>
    public required SessionType SessionType { get; init; }

    /// <summary>UTC time the session started.</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>UTC time the session ended, if it has ended.</summary>
    public DateTimeOffset? EndedAtUtc { get; init; }

    /// <summary>Name of the player/driver, if known.</summary>
    /// <remarks>
    /// A mutable label, not an identity — players rename themselves. <see cref="SimDriverId"/> is
    /// what ties sessions from the same person together across a rename; this is recorded per
    /// session so the name used at the time is not lost when the person later changes it.
    /// </remarks>
    public string? PlayerName { get; init; }

    /// <summary>
    /// The sim's own stable identifier for the player, if known — a durable account id rather
    /// than a display name, so a session can still be attributed to the right person after they
    /// rename themselves. Only unique within the sim that issued it: two sims can hand out the
    /// same numeric id to different people, so consumers must scope it by
    /// <see cref="GameVersion"/>'s game. See <see cref="SimCarId"/> for the same convention
    /// applied to cars.
    /// </summary>
    public string? SimDriverId { get; init; }

    /// <summary>Name of the car driven, if known.</summary>
    public string? CarName { get; init; }

    /// <summary>Name of the car's class, if known.</summary>
    public string? CarClassName { get; init; }

    /// <summary>Name of the car's manufacturer, if known.</summary>
    public string? ManufacturerName { get; init; }

    /// <summary>
    /// The sim's own internal identifier for the car driven, if known. Populated even when
    /// <see cref="CarName"/> isn't — some sims (e.g. RaceRoom) expose only numeric car/class/
    /// manufacturer ids over their telemetry API, with no in-process way to resolve them to names.
    /// </summary>
    public string? SimCarId { get; init; }

    /// <summary>The sim's own internal identifier for the car's class, if known. See <see cref="SimCarId"/>.</summary>
    public string? SimCarClassId { get; init; }

    /// <summary>The sim's own internal identifier for the car's manufacturer, if known. See <see cref="SimCarId"/>.</summary>
    public string? SimManufacturerId { get; init; }

    /// <summary>
    /// How fast this session consumed fuel relative to real time, as the sim's own raw rate code.
    /// Like <see cref="SessionType"/>, collectors carry this through untranslated rather than
    /// normalizing it to a multiplier — the encoding is sim-specific (RaceRoom uses
    /// <c>-1</c> = not available, <c>0</c> = consumption off, <c>1</c>–<c>4</c> = 1x–4x), and the
    /// mapping to a canonical rate belongs to an analysis pass that knows which sim produced it.
    /// <see langword="null"/> only when the source has no such concept at all.
    /// </summary>
    /// <remarks>
    /// Recorded because it changes what the session's fuel data means: a lap at 4x burns four
    /// times the fuel of the same lap at 1x, so sessions run under different rates are not
    /// comparable inputs to a fuel model. Independent of <see cref="TyreWearRate"/> — sims let
    /// the two be configured separately.
    /// </remarks>
    public int? FuelUsageRate { get; init; }

    /// <summary>
    /// How fast this session wore tyres relative to real time, as the sim's own raw rate code.
    /// Carried through untranslated for the same reason as <see cref="FuelUsageRate"/>, and using
    /// the same sim-specific encoding. Independent of it: a session can run accelerated tyre wear
    /// with fuel consumption switched off entirely.
    /// </summary>
    public int? TyreWearRate { get; init; }

    /// <summary>Simulator-specific session metadata with no canonical equivalent (e.g. weather, setup).</summary>
    public JsonElement Extras { get; init; }
}

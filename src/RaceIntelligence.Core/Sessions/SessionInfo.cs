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

    /// <summary>The type of session.</summary>
    public required SessionType SessionType { get; init; }

    /// <summary>UTC time the session started.</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>UTC time the session ended, if it has ended.</summary>
    public DateTimeOffset? EndedAtUtc { get; init; }

    /// <summary>Name of the player/driver, if known.</summary>
    public string? PlayerName { get; init; }

    /// <summary>Name of the car driven, if known.</summary>
    public string? CarName { get; init; }

    /// <summary>Name of the car's class, if known.</summary>
    public string? CarClassName { get; init; }

    /// <summary>Name of the car's manufacturer, if known.</summary>
    public string? ManufacturerName { get; init; }

    /// <summary>Simulator-specific session metadata with no canonical equivalent (e.g. weather, setup).</summary>
    public JsonElement Extras { get; init; }
}

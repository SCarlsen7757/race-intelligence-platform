using System.Text.Json;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Persistence.Entities;

/// <summary>
/// A single telemetry-collection session: the car, track, simulator build, and capabilities that
/// were in effect for every <see cref="Lap"/> and <see cref="TelemetrySample"/> it produced.
/// </summary>
/// <remarks>
/// The persisted form of <see cref="RaceIntelligence.Core.Sessions.SessionInfo"/>. Unlike the Core
/// type, the id is the primary key here and every reference (driver, track layout, car, game
/// version) is a foreign key rather than a denormalized name — resolved once via the
/// <c>Repositories/</c> resolve-or-create helpers when a session is first persisted.
/// </remarks>
public sealed class Session
{
    /// <summary>
    /// Primary key. Generated in application code via <see cref="Guid.CreateVersion7"/> — normally
    /// this is the same id the collector assigned to the originating
    /// <see cref="RaceIntelligence.Core.Sessions.SessionInfo"/>, since telemetry samples reference
    /// the session id before the session row is necessarily persisted.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>The exact game build / telemetry API version / connector version that produced this session.</summary>
    public Guid GameVersionId { get; set; }

    /// <summary>Navigation to the <see cref="Entities.GameVersion"/>.</summary>
    public GameVersion? GameVersion { get; set; }

    /// <summary>The driver, if known.</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Navigation to the <see cref="Entities.Driver"/>, if known.</summary>
    public Driver? Driver { get; set; }

    /// <summary>The track layout used, if known.</summary>
    public Guid? TrackLayoutId { get; set; }

    /// <summary>Navigation to the <see cref="Entities.TrackLayout"/>, if known.</summary>
    public TrackLayout? TrackLayout { get; set; }

    /// <summary>The car driven, if known.</summary>
    public Guid? CarId { get; set; }

    /// <summary>Navigation to the <see cref="Entities.Car"/>, if known.</summary>
    public Car? Car { get; set; }

    /// <summary>
    /// The sim's own internal identifier for the car driven, if known. Populated even when
    /// <see cref="CarId"/> isn't — some sims expose only numeric car/class/manufacturer ids with
    /// no in-process way to resolve them to names, so this is stored raw rather than dropped.
    /// </summary>
    public string? SimCarId { get; set; }

    /// <summary>The sim's own internal identifier for the car's class, if known. See <see cref="SimCarId"/>.</summary>
    public string? SimCarClassId { get; set; }

    /// <summary>The sim's own internal identifier for the car's manufacturer, if known. See <see cref="SimCarId"/>.</summary>
    public string? SimManufacturerId { get; set; }

    /// <summary>
    /// The type of session. Collectors that perform no analysis (the norm) store the sim's raw
    /// session-type value here unmapped, reinterpreted through this enum's underlying type rather
    /// than translated to its named values — the correct mapping depends on which sim produced it.
    /// A later analysis pass (which does know the sim) is expected to rewrite this to the canonical
    /// numbering. Stored as <c>smallint</c>, not a native Postgres enum — see
    /// <see cref="TelemetrySample"/> remarks for why.
    /// </summary>
    public SessionType SessionType { get; set; }

    /// <summary>
    /// The capabilities available from this session's source, stored as the raw <see cref="ulong"/>
    /// flags bit pattern reinterpreted into a <c>bigint</c> column. See
    /// <c>Converters/SimCapabilitiesConverter.cs</c>.
    /// </summary>
    public SimCapabilities Capabilities { get; set; }

    /// <summary>
    /// The schema/shape version of the canonical telemetry model this session's samples conform
    /// to, independent of <see cref="GameVersionId"/> — bumped only when the platform's own
    /// canonical model changes.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>Weather conditions for this session, if captured. Simulator-specific shape, stored as jsonb.</summary>
    public JsonElement? Weather { get; set; }

    /// <summary>Car setup used for this session, if captured. Simulator-specific shape, stored as jsonb.</summary>
    public JsonElement? Setup { get; set; }

    /// <summary>Simulator-specific session metadata with no canonical equivalent, stored as jsonb.</summary>
    public JsonElement Extras { get; set; }

    /// <summary>UTC time the session started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>UTC time the session ended, if it has ended.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Per-lap summary statistics recorded for this session.</summary>
    public ICollection<Lap> Laps { get; set; } = new List<Lap>();

    /// <summary>Raw telemetry samples recorded for this session.</summary>
    public ICollection<TelemetrySample> TelemetrySamples { get; set; } = new List<TelemetrySample>();
}

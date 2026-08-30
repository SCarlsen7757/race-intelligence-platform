using System.Text.Json;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Persistence.Core.Entities;

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

    public GameVersion? GameVersion { get; set; }

    /// <summary>The driver, if known.</summary>
    public Guid? DriverId { get; set; }

    public Driver? Driver { get; set; }

    /// <summary>
    /// The player/driver name as reported for <i>this</i> session, if known. Kept alongside
    /// <see cref="DriverId"/> because <see cref="Entities.Driver.DisplayName"/> tracks the driver's
    /// latest name and is rewritten on rename — this is what preserves the name actually in use at
    /// the time the session ran.
    /// </summary>
    public string? PlayerName { get; set; }

    /// <summary>The track layout used, if known.</summary>
    public Guid? TrackLayoutId { get; set; }

    public TrackLayout? TrackLayout { get; set; }

    /// <summary>The car driven, if known.</summary>
    public Guid? CarId { get; set; }

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
    /// How fast this session consumed fuel relative to real time, stored as the sim's own raw rate
    /// code rather than a normalized multiplier — like <see cref="SessionType"/>, the encoding is
    /// sim-specific (RaceRoom: <c>-1</c> = not available, <c>0</c> = off, <c>1</c>–<c>4</c> = 1x–4x)
    /// and reinterpreting it belongs to a later sim-aware analysis pass. Stored as <c>smallint</c>.
    /// <see langword="null"/> only when the source has no such concept at all.
    /// </summary>
    /// <remarks>
    /// Recorded because it changes what the session's fuel data means: sessions run under different
    /// rates are not comparable inputs to a fuel model. Independent of <see cref="TyreWearRate"/>.
    /// </remarks>
    public int? FuelUsageRate { get; set; }

    /// <summary>
    /// How fast this session wore tyres relative to real time, stored as the sim's own raw rate
    /// code using the same sim-specific encoding and for the same reason as
    /// <see cref="FuelUsageRate"/>. Independent of it — a session can run accelerated tyre wear
    /// with fuel consumption switched off entirely. Stored as <c>smallint</c>.
    /// </summary>
    public int? TyreWearRate { get; set; }

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

    // Weather and setup used to be jsonb columns here. They were NULL on every row of every
    // session ever recorded, and always would be: RaceRoom has no dynamic weather and exports none,
    // and it has no readable setup export in any form a connector could persist. They were not
    // channels waiting to be captured — they were capabilities the simulator does not offer, and a
    // column that can only ever be null documents a feature that does not exist (#109).

    /// <summary>Simulator-specific session metadata with no canonical equivalent, stored as jsonb.</summary>
    public JsonElement Extras { get; set; }

    /// <summary>UTC time the session started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>UTC time the session ended, if it has ended.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Per-lap summary statistics recorded for this session.</summary>
    public ICollection<Lap> Laps { get; set; } = new List<Lap>();

    // No telemetry navigation. The sample is RaceRoom's type in RaceRoom's assembly since #109,
    // and this one is shared; the foreign key is configured from that side instead.
}

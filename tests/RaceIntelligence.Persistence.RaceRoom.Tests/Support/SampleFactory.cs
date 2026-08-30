using System.Text.Json;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Persistence.Core.Repositories;

namespace RaceIntelligence.Persistence.RaceRoom.Tests.Support;

/// <summary>Builders for canonical Core objects and persisted prerequisite rows, kept small and explicit for test readability.</summary>
internal static class SampleFactory
{
    /// <summary>
    /// A name nothing else in the shared container will have claimed.
    /// </summary>
    /// <remarks>
    /// These tests used to isolate themselves by giving each one its own <i>game</i>, because every
    /// unique key was scoped by <c>game_id</c>. The database is the simulator now (ADR 0001), so
    /// that scoping is gone and two tests both resolving "Suzuka" would legitimately get the same
    /// row. Isolation therefore comes from the names themselves.
    /// </remarks>
    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Builds a <see cref="GameVersionIdentity"/> no other test will resolve to the same row.
    /// </summary>
    /// <remarks>
    /// Uniqueness now rides on the connector version, since <c>game_versions</c> is keyed on
    /// <c>(game_version, api_version_major, api_version_minor, connector_version)</c> and no longer
    /// on the game. <see cref="GameVersionIdentity.Game"/> is still populated because the canonical
    /// type requires it and the ingest API checks it — storage simply ignores it.
    /// </remarks>
    public static GameVersionIdentity UniqueGameVersion(string? connectorVersion = null) =>
        new()
        {
            Game = WellKnownGames.RaceRoom,
            GameVersion = "1.2.3",
            ApiVersionMajor = 1,
            ApiVersionMinor = 0,
            ConnectorVersion = connectorVersion ?? Unique("connector"),
        };

    /// <summary>Persists a game version and a session that references it, returning the session id.</summary>
    public static async Task<Guid> CreateSessionAsync(RaceRoomDbContext db, JsonElement? extras = null)
    {
        var repo = new GameVersionRepository(db);
        var version = await repo.ResolveOrCreateAsync(UniqueGameVersion()).ConfigureAwait(false);

        var session = new RaceIntelligence.Persistence.Core.Entities.Session
        {
            Id = Guid.CreateVersion7(),
            GameVersionId = version.Id,
            SessionType = SessionType.Race,
            Capabilities = SimCapabilities.TyreWear | SimCapabilities.TyrePressure,
            SchemaVersion = 1,
            Extras = extras ?? EmptyObject(),
            StartedAt = DateTimeOffset.UtcNow,
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return session.Id;
    }

    /// <summary>An empty JSON object, the neutral value for session <c>Extras</c> in tests that don't care about its contents.</summary>
    public static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement;

    /// <summary>The same neutral value for telemetry-sample <c>Extras</c>, which is raw JSON text.</summary>
    public const string EmptyObjectText = "{}";

    /// <summary>A non-trivial JSON object for jsonb round-trip assertions: nested object, array, numbers, bool, null.</summary>
    public const string NonTrivialExtrasText =
        """
        {
            "pushToPass": { "available": true, "usesRemaining": 5 },
            "tags": ["yellow-flag", "traffic"],
            "correctionFactor": 1.0625,
            "note": null
        }
        """;

    /// <inheritdoc cref="NonTrivialExtrasText"/>
    /// <remarks>Element form, for session <c>Extras</c>, which is still a <see cref="JsonElement"/>.</remarks>
    public static JsonElement NonTrivialExtras() => JsonDocument.Parse(NonTrivialExtrasText).RootElement;

    /// <summary>Builds a telemetry sample for <paramref name="sessionId"/> at <paramref name="sequenceNumber"/>.</summary>
    /// <remarks>
    /// Asymmetric across the corners throughout, so a column written at the wrong position — the one
    /// failure a binary <c>COPY</c> will not report — shows up as a wrong value rather than passing.
    /// Channels not set here are left null, which is what "the simulator did not report this" means.
    /// </remarks>
    public static RaceRoomTelemetrySample TelemetrySample(Guid sessionId, long sequenceNumber, DateTimeOffset? timestamp = null) => new()
    {
        SessionId = sessionId,
        SequenceNumber = sequenceNumber,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow.AddMilliseconds(sequenceNumber),
        SimulationTime = sequenceNumber * 0.1,
        Speed = 45.5f,
        Throttle = 0.8f,
        Brake = 0f,
        Steering = 0.1f,
        Gear = 4,
        EngineRpm = 6500f,
        FuelLeft = 40.2f,
        LapNumber = 1,
        Sector = 1,
        Position = 3,
        TrackPositionFraction = 0.42f,
        WheelSpeedFl = 45.1f,
        WheelSpeedFr = 45.2f,
        WheelSpeedRl = 44.9f,
        WheelSpeedRr = 45.0f,
        SuspensionTravelFl = 0.05f,
        SuspensionTravelFr = 0.051f,
        SuspensionTravelRl = 0.06f,
        SuspensionTravelRr = 0.061f,
        TyrePressureFl = 180f,
        TyrePressureFr = 181f,
        TyrePressureRl = 175f,
        TyrePressureRr = 176f,
        TyreWearFl = 0.1f,
        TyreWearFr = 0.11f,
        TyreWearRl = 0.12f,
        // Left unreported so an absent corner can be told from a brand-new tyre.
        TyreWearRr = null,
        TyreTempFlInner = 85f,
        TyreTempFlMiddle = 90f,
        TyreTempFlOuter = 88f,
        TyreTempFrInner = 86f,
        TyreTempFrMiddle = 91f,
        TyreTempFrOuter = 89f,
        TyreTempRlInner = 84f,
        TyreTempRlMiddle = 89f,
        TyreTempRlOuter = 87f,
        TyreTempRrInner = 85f,
        TyreTempRrMiddle = 90f,
        TyreTempRrOuter = 88f,
        BrakeTempFl = 401f,
        BrakeTempFr = 402f,
        BrakeTempRl = 403f,
        BrakeTempRr = 404f,
        CamberFl = -0.06f,
        CamberFr = -0.05f,
        CamberRl = -0.04f,
        CamberRr = -0.03f,
        WorldPositionX = 1234.5,
        WorldPositionY = 67.25,
        WorldPositionZ = -890.75,
        AccelerationLongitudinal = -8.5f,
        AccelerationLateral = 12.25f,
        DamageEngine = 0.5f,
        PushToPassAmountLeft = 4,
        DrsActivationsLeft = 3,
        DrsActivationsUnlimited = false,
        AbsActive = true,
        TractionControlActive = false,
        TyreSubtypeFront = 2,
        TyreSubtypeRear = 4,
        CutTrackWarnings = 1,
        FlagYellow = 0,
    };

    /// <summary>One operating window per corner, with distinct bounds so a copied index shows.</summary>
    public static IReadOnlyList<OperatingWindow> OperatingWindows() =>
    [
        new(Corner.FrontLeft, Compound: 2, 90f, 70f, 110f, 410f, 200f, 600f),
        new(Corner.FrontRight, Compound: 2, 91f, 71f, 111f, 411f, 201f, 601f),
        new(Corner.RearLeft, Compound: 4, 92f, 72f, 112f, 412f, 202f, 602f),
        new(Corner.RearRight, Compound: 4, 93f, 73f, null, 413f, 203f, null),
    ];

    /// <summary>
    /// Builds <paramref name="count"/> sequential telemetry samples for <paramref name="sessionId"/>
    /// starting at <paramref name="startSequence"/>. Each sample's timestamp is derived
    /// deterministically from <paramref name="anchor"/> and its own sequence number (rather than
    /// wall-clock time at call time), so two independently-built batches that share a sequence
    /// number also share that sample's primary key — required for the bulk writer's duplicate
    /// detection to be exercised meaningfully across overlapping batches in tests.
    /// </summary>
    public static IReadOnlyList<RaceRoomTelemetrySample> TelemetryBatch(Guid sessionId, int count, long startSequence = 0, DateTimeOffset? anchor = null)
    {
        var start = anchor ?? DateTimeOffset.UtcNow;
        var samples = new List<RaceRoomTelemetrySample>(count);
        for (var i = 0; i < count; i++)
        {
            var sequenceNumber = startSequence + i;
            samples.Add(TelemetrySample(sessionId, sequenceNumber, start.AddMilliseconds(sequenceNumber * 20)));
        }

        return samples;
    }
}

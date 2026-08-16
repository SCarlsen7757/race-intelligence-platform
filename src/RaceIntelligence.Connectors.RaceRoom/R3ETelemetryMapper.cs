using System.Buffers;
using System.Text;
using System.Text.Json;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Connectors.RaceRoom;

/// <summary>
/// Translates raw <see cref="R3ESharedRaw"/> shared-memory snapshots into the platform's
/// canonical telemetry model. Pure translation — it performs no analysis, matching the collector
/// design principle that raw telemetry collection and analysis are separate concerns.
/// </summary>
/// <remarks>
/// Not allocation-free: every sample necessarily allocates a <see cref="TelemetrySample"/> and a
/// detached <see cref="JsonElement"/> for its <c>Extras</c>. What it does avoid is allocating the
/// machinery to produce them — the JSON scratch buffer and writer are reused across samples (see
/// <see cref="RentExtrasWriter"/>).
/// </remarks>
internal static class R3ETelemetryMapper
{
    /// <summary>
    /// Converts a raw sentinel-encoded value to <see langword="null"/>. RaceRoom uses
    /// <c>-1.0</c> (or, for arrays, <c>-1.0</c> per element) to mean "not available" on several
    /// fields — coercing that to <c>0</c> would silently corrupt downstream analysis (e.g. a
    /// fuel/tyre-wear reading of "unavailable" is not the same as "empty"/"unworn"), and because
    /// raw telemetry is stored permanently, such a mistake would be baked into history forever.
    /// </summary>
    /// <remarks>
    /// Only apply this to fields the header documents as using <c>-1</c>/<c>-1.0</c> as an N/A
    /// sentinel. Some RaceRoom fields are legitimately negative in normal operation —
    /// <c>steer_input_raw</c> ranges -1..1, and <c>gear</c> uses -1 (reverse) and -2 (N/A) as
    /// meaningful values, not sentinels — and must never be passed through this helper.
    /// </remarks>
    private static float? NullIfNegative(float value) => value < 0f ? null : value;

    private static int? NullIfNegative(int value) => value < 0 ? null : value;

    /// <summary>
    /// Converts RaceRoom's <c>tire_wear</c> to the canonical
    /// <see cref="TelemetrySample.TyreWear"/> convention.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Despite its name, RaceRoom reports tread <i>remaining</i>: the value starts at 1.0 on fresh
    /// tyres and falls as they wear. The header documents only "Range 0.0-1.0", so this was read
    /// off real telemetry — a 24-lap stint moved from 0.9979 to 0.8098, monotonically down.
    /// </para>
    /// <para>
    /// The canonical field is the other way round (0 = new, 1 = fully worn), so this inverts. That
    /// direction is deliberate: "wear" that decreases as tyres wear out would invert the sign of
    /// every degradation rate computed from it.
    /// </para>
    /// <para>
    /// The sentinel check happens first, and must: <c>-1.0</c> means "not available", and inverting
    /// it before testing would turn it into a confident, entirely fictional <c>2.0</c>.
    /// </para>
    /// </remarks>
    private static float? TreadRemainingToWear(float treadRemaining) =>
        NullIfNegative(treadRemaining) is { } remaining ? 1f - remaining : null;

    /// <summary>RaceRoom's <c>gear</c> value for "not available", distinct from -1 (reverse).</summary>
    private const int GearNotAvailable = -2;

    /// <summary>
    /// Converts a non-positive id to <see langword="null"/>. Distinct from
    /// <see cref="NullIfNegative(int)"/>, which lets <c>0</c> through: for identity fields <c>0</c>
    /// is not a usable value, it is RaceRoom's "no account" marker.
    /// </summary>
    private static int? NullIfNotPositive(int value) => value > 0 ? value : null;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>Converts engine speed from RaceRoom's native rad/s to RPM.</summary>
    internal static float RadiansPerSecondToRpm(float radiansPerSecond) => radiansPerSecond * 60f / (2f * MathF.PI);

    /// <summary>
    /// Decodes a UTF-8, NUL-terminated 64-byte name buffer. Finds the first NUL and decodes
    /// exactly that slice — decoding all 64 bytes and then trimming would mishandle a multi-byte
    /// UTF-8 sequence that gets truncated right at the buffer edge (the trailing bytes of a
    /// partial sequence are garbage, not padding). If there is no NUL at all (a name that fills
    /// every byte), the entire buffer is decoded verbatim.
    /// </summary>
    internal static string DecodeUtf8Name(Utf8Name64 name)
    {
        ReadOnlySpan<byte> bytes = name;
        int nulIndex = bytes.IndexOf((byte)0);
        ReadOnlySpan<byte> content = nulIndex >= 0 ? bytes[..nulIndex] : bytes;
        return Encoding.UTF8.GetString(content);
    }


    /// <remarks>
    /// Tread and window temperatures use the same <c>-1.0 = N/A</c> convention as the rest of the
    /// shared memory block, so they get the same sentinel treatment. Passing -1 through would look
    /// like a plausible sub-zero tread reading and quietly corrupt tyre degradation analysis.
    /// </remarks>
    private static TyreTemperature MapTyreTemperature(R3ETireTemp t) =>
        new(Inner: NullIfNegative(t.CurrentTemp[0]),
            Middle: NullIfNegative(t.CurrentTemp[1]),
            Outer: NullIfNegative(t.CurrentTemp[2]),
            Optimal: NullIfNegative(t.OptimalTemp),
            Cold: NullIfNegative(t.ColdTemp),
            Hot: NullIfNegative(t.HotTemp));

    /// <summary>Builds a canonical telemetry sample from one raw shared-memory snapshot.</summary>
    public static TelemetrySample ToSample(in R3ESharedRaw raw, Guid sessionId, long sequenceNumber, DateTimeOffset timestamp)
    {
        var wheelSpeed = new WheelData<float>(raw.TireSpeed[0], raw.TireSpeed[1], raw.TireSpeed[2], raw.TireSpeed[3]);

        // "Suspension travel" is best represented by suspension_deflection (r3e_playerdata); the
        // header documents the unit for that whole block collectively as "radians, meters, meters
        // per second" without labelling each field individually — deflection is the one measured
        // in meters, matching Core's WheelData<float> "meters" documentation.
        var suspensionTravel = new WheelData<float>(
            (float)raw.Player.SuspensionDeflection[0],
            (float)raw.Player.SuspensionDeflection[1],
            (float)raw.Player.SuspensionDeflection[2],
            (float)raw.Player.SuspensionDeflection[3]);

        var tyreTemperature = new WheelData<TyreTemperature>(
            MapTyreTemperature(raw.TireTemp[0]),
            MapTyreTemperature(raw.TireTemp[1]),
            MapTyreTemperature(raw.TireTemp[2]),
            MapTyreTemperature(raw.TireTemp[3]));

        var tyrePressure = new WheelData<float?>(
            NullIfNegative(raw.TirePressure[0]),
            NullIfNegative(raw.TirePressure[1]),
            NullIfNegative(raw.TirePressure[2]),
            NullIfNegative(raw.TirePressure[3]));

        var tyreWear = new WheelData<float?>(
            TreadRemainingToWear(raw.TireWear[0]),
            TreadRemainingToWear(raw.TireWear[1]),
            TreadRemainingToWear(raw.TireWear[2]),
            TreadRemainingToWear(raw.TireWear[3]));

        return new TelemetrySample
        {
            SessionId = sessionId,
            SequenceNumber = sequenceNumber,
            Timestamp = timestamp,
            SimulationTime = raw.Player.GameSimulationTime,
            Speed = raw.CarSpeed,
            Throttle = NullIfNegative(raw.Throttle),
            Brake = NullIfNegative(raw.Brake),
            Steering = raw.SteerInputRaw, // -1..1 is a legitimate range, not an N/A sentinel.
            // Not NullIfNegative: -1 is reverse, a real gear. Only -2 means "not available".
            Gear = raw.Gear == GearNotAvailable ? null : raw.Gear,
            EngineRpm = RadiansPerSecondToRpm(raw.EngineRps),
            FuelLeft = raw.FuelLeft,
            // completed_laps is 0-indexed ("6 means the car is on its 7th lap"); the canonical
            // model wants the 1-indexed lap currently being driven. completed_laps == -1 (N/A,
            // e.g. before a session is meaningfully underway) maps defensively to lap 1.
            LapNumber = raw.CompletedLaps < 0 ? 1 : raw.CompletedLaps + 1,
            Sector = raw.TrackSector,
            Position = raw.Position,
            WheelSpeed = wheelSpeed,
            SuspensionTravel = suspensionTravel,
            TyreTemperature = tyreTemperature,
            TyrePressure = tyrePressure,
            TyreWear = tyreWear,
            TrackPositionFraction = NullIfNegative(raw.LapDistanceFraction),
            Extras = BuildSampleExtras(in raw),
        };
    }

    /// <summary>Builds the canonical session record for a session that just started.</summary>
    public static SessionInfo ToSessionInfo(in R3ESharedRaw raw, Guid sessionId, GameVersionIdentity gameVersion, SimCapabilities capabilities, DateTimeOffset startedAtUtc)
    {
        return new SessionInfo
        {
            SessionId = sessionId,
            GameVersion = gameVersion,
            Capabilities = capabilities,
            TrackName = DecodeUtf8Name(raw.TrackName),
            LayoutName = DecodeUtf8Name(raw.LayoutName),
            LayoutLengthMeters = raw.LayoutLength > 0f ? raw.LayoutLength : null,
            // The connector performs no analysis (see file remarks), so it does not translate
            // RaceRoom's raw session_type into the canonical enum's own numbering — that mapping
            // is really a property of the game/analysis layer, not the collector, since it depends
            // on which sim produced the value. The raw sim int is carried through as-is (reinterpreted
            // through the enum's underlying type); a later analysis pass, which knows the sim, is
            // expected to rewrite it to the canonical numbering.
            SessionType = (SessionType)raw.SessionType,
            StartedAtUtc = startedAtUtc,
            PlayerName = NullIfEmpty(DecodeUtf8Name(raw.PlayerName)),
            // The stable account id behind that display name. VehicleInfo.UserId is the driver
            // entry for the player's own slot and is preferred; Player.UserId is the fallback for
            // snapshots where only the player block carries it. Both are tested with > 0, NOT
            // >= 0: RaceRoom reports 0 or -1 when the session is offline/unauthenticated, and 0 is
            // not a real account id — passing it through as "0" would silently merge every offline
            // session of every driver into a single identity. That is why NullIfNegative, which
            // deliberately lets 0 through for genuine numeric fields, is not used here.
            SimDriverId = (NullIfNotPositive(raw.VehicleInfo.UserId) ?? NullIfNotPositive(raw.Player.UserId))?.ToString(),
            // Likewise, RaceRoom's shared memory exposes only numeric car/class/manufacturer ids
            // (VehicleInfo.ClassId/ModelId/ManufacturerId) — never human-readable names, and there
            // is no in-memory lookup table. These stay null; the raw ids are carried below instead.
            CarName = null,
            CarClassName = null,
            ManufacturerName = null,
            SimCarId = NullIfNegative(raw.VehicleInfo.ModelId)?.ToString(),
            SimCarClassId = NullIfNegative(raw.VehicleInfo.ClassId)?.ToString(),
            SimManufacturerId = NullIfNegative(raw.VehicleInfo.ManufacturerId)?.ToString(),
            // Carried through raw, exactly like SessionType above, and with no sentinel filtering:
            // RaceRoom encodes these as -1 = N/A, 0 = off, 1-4 = 1x-4x, so -1 stays -1 rather than
            // becoming null. Turning a sim-specific rate code into a canonical multiplier is the
            // analysis layer's job, not the collector's. The two settings are independent — a
            // session can run accelerated tyre wear with fuel consumption switched off entirely.
            FuelUsageRate = raw.FuelUseActive,
            TyreWearRate = raw.TireWearActive,
            Extras = BuildSessionExtras(in raw),
        };
    }

    /// <summary>
    /// Converts a RaceRoom lap/sector time in seconds to a <see cref="TimeSpan"/>, honouring the
    /// <c>-1.0 = N/A</c> sentinel the timing fields share with the rest of the block.
    /// </summary>
    private static TimeSpan? SecondsToTimeSpan(float seconds) =>
        seconds < 0f ? null : TimeSpan.FromSeconds(seconds);

    /// <summary>
    /// Converts a <c>sector_time_*</c> triple to canonical cumulative splits, normalising whichever
    /// convention the running game turns out to use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DriverStanding.CurrentSectorTimes"/> is defined as cumulative, so a game found to
    /// publish per-sector durations is converted here — this is the one place that knows, and every
    /// consumer downstream sees a single convention. See <see cref="R3ESectorTimeConvention"/> for
    /// why the convention has to be discovered rather than assumed.
    /// </para>
    /// <para>
    /// A running sum cannot simply skip a missing entry: if sector 1 is unreported, no later
    /// cumulative split can be reconstructed, and inventing one would understate the lap. Once a
    /// gap appears, everything after it is <see langword="null"/>.
    /// </para>
    /// <para>
    /// A fresh array is allocated per driver per snapshot; at the standings rate that is a few
    /// hundred small arrays a second, well below the point where pooling would earn its complexity.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<TimeSpan?> MapSectorTimes(Float3 sectors, R3ESectorTimeConvention convention)
    {
        if (convention == R3ESectorTimeConvention.Cumulative)
        {
            return [SecondsToTimeSpan(sectors[0]), SecondsToTimeSpan(sectors[1]), SecondsToTimeSpan(sectors[2])];
        }

        var splits = new TimeSpan?[3];
        float running = 0f;
        for (int i = 0; i < 3; i++)
        {
            if (sectors[i] < 0f)
            {
                break;
            }

            running += sectors[i];
            splits[i] = TimeSpan.FromSeconds(running);
        }

        return splits;
    }

    /// <summary>The lap total a sector triple implies: its last cumulative split.</summary>
    private static TimeSpan? LapTimeFromSectors(IReadOnlyList<TimeSpan?> cumulativeSplits) =>
        cumulativeSplits.Count > 0 ? cumulativeSplits[^1] : null;

    /// <summary>
    /// Counts the cut-track penalty types currently outstanding.
    /// </summary>
    /// <remarks>
    /// r3e.h documents each member of <see cref="R3ECutTrackPenalties"/> as "-1.0 = none pending,
    /// otherwise penalty time dep on penalty type". So <c>-1</c> here means <i>clean</i>, not
    /// <i>unknown</i>, and a count of zero is a real answer rather than a sentinel leak — unlike
    /// almost everywhere else in this block.
    /// </remarks>
    private static int CountPendingPenalties(in R3ECutTrackPenalties penalties)
    {
        int count = 0;
        if (penalties.DriveThrough >= 0f) { count++; }
        if (penalties.StopAndGo >= 0f) { count++; }
        if (penalties.PitStop >= 0f) { count++; }
        if (penalties.TimeDeduction >= 0f) { count++; }
        if (penalties.SlowDown >= 0f) { count++; }
        return count;
    }

    /// <summary>Builds one canonical timing-tower row from a raw driver array entry.</summary>
    /// <remarks>
    /// Everything here is scoring-granularity by necessity: RaceRoom's per-driver entry carries no
    /// pedal inputs, tyre state, fuel or damage for anyone. Those exist only in the root of the
    /// block, describing the car this machine is running — see <see cref="DriverStanding"/>.
    /// </remarks>
    internal static DriverStanding ToDriverStanding(in R3EDriverData driver, R3ESectorTimeConvention convention)
    {
        var currentSectors = MapSectorTimes(driver.SectorTimeCurrentSelf, convention);
        var previousSectors = MapSectorTimes(driver.SectorTimePreviousSelf, convention);
        var bestSectors = MapSectorTimes(driver.SectorTimeBestSelf, convention);

        return new DriverStanding
        {
            // Same > 0 test, and the same reason, as SessionInfo.SimDriverId above: RaceRoom
            // reports 0 or -1 for an offline/unauthenticated slot, and treating "0" as an identity
            // would merge every such driver into one person when views from several machines are
            // matched up.
            SimDriverId = NullIfNotPositive(driver.DriverInfo.UserId)?.ToString(),
            SlotId = NullIfNegative(driver.DriverInfo.SlotId),
            DisplayName = DecodeUtf8Name(driver.DriverInfo.Name),
            CarNumber = NullIfNegative(driver.DriverInfo.CarNumber),
            SimCarId = NullIfNegative(driver.DriverInfo.ModelId)?.ToString(),
            SimCarClassId = NullIfNegative(driver.DriverInfo.ClassId)?.ToString(),
            SimManufacturerId = NullIfNegative(driver.DriverInfo.ManufacturerId)?.ToString(),
            Position = NullIfNotPositive(driver.Place),
            PositionInClass = NullIfNotPositive(driver.PlaceClass),
            // r3e.h: "How many laps the car has completed. If this value is 6, the car is on it's
            // 7th lap. -1 = n/a". A slot that is not yet active reports the sentinel, and flooring
            // it to zero would state as fact that the car has completed no laps.
            CompletedLaps = NullIfNegative(driver.CompletedLaps),
            TrackPositionFraction = NullIfNegative(driver.LapDistanceFraction),
            // r3e.h documents no base and no sentinel for track_sector. Treated as 1-based with 0
            // meaning "not reported", which is what the field is observed to do — it reads 0 before
            // the car crosses the line, then 1, 2, 3.
            //
            // NOTE: ToSample passes the root block's track_sector through raw and unfiltered, so a
            // sample can carry 0 or -1 where a standing carries null. The two disagree today. This
            // side is the one that can change freely; the sample side feeds a permanent archive, so
            // reconciling them is a deliberate decision rather than a cleanup.
            Sector = NullIfNotPositive(driver.TrackSector),
            // car_speed carries no documented sentinel, unlike most of the block. Filtered anyway
            // because it is a magnitude and a negative reading could only be an unreported value.
            Speed = NullIfNegative(driver.CarSpeed),
            CurrentLapTime = SecondsToTimeSpan(driver.LapTimeCurrentSelf),
            // The driver array has no lap_time_previous_self or lap_time_best_self field. A lap's
            // total is its final cumulative split, which MapSectorTimes has already normalised.
            PreviousLapTime = LapTimeFromSectors(previousSectors),
            BestLapTime = LapTimeFromSectors(bestSectors),
            // -1 = N/A, so a plain == 1 test rather than != 0.
            CurrentLapValid = driver.CurrentLapValid < 0 ? null : driver.CurrentLapValid == 1,
            CurrentSectorTimes = currentSectors,
            PreviousSectorTimes = previousSectors,
            BestSectorTimes = bestSectors,
            GapToCarAhead = SecondsToTimeSpan(driver.TimeDeltaFront),
            GapToCarBehind = SecondsToTimeSpan(driver.TimeDeltaBehind),
            InPitLane = driver.InPitlane < 0 ? null : driver.InPitlane == 1,
            PitStopStatus = Enum.IsDefined((PitStopStatus)driver.PitStopStatus)
                ? (PitStopStatus)driver.PitStopStatus
                : PitStopStatus.Unavailable,
            PitStopCount = NullIfNegative(driver.NumPitstops),
            FinishStatus = Enum.IsDefined((DriverFinishStatus)driver.FinishStatus)
                ? (DriverFinishStatus)driver.FinishStatus
                : DriverFinishStatus.Unavailable,
            PenaltyCount = CountPendingPenalties(in driver.Penalties),
        };
    }

    /// <summary>Builds a canonical standings snapshot from the raw driver array.</summary>
    /// <param name="drivers">The entries read for this frame, already trimmed to the cars actually present.</param>
    /// <param name="raw">The snapshot the array was read alongside, supplying the local car's identity and the sim clock.</param>
    /// <param name="sessionId">The session the snapshot belongs to.</param>
    /// <param name="capturedAtUtc">UTC time of capture, as observed by the connector.</param>
    /// <param name="convention">
    /// How this game fills its sector triples, as established by
    /// <see cref="R3ESectorTimeConventionDetector"/>. Applies to every car, since one game publishes
    /// one convention.
    /// </param>
    public static SessionStandings ToSessionStandings(
        ReadOnlySpan<R3EDriverData> drivers,
        in R3ESharedRaw raw,
        Guid sessionId,
        DateTimeOffset capturedAtUtc,
        R3ESectorTimeConvention convention)
    {
        var standings = new DriverStanding[drivers.Length];
        for (int i = 0; i < drivers.Length; i++)
        {
            standings[i] = ToDriverStanding(in drivers[i], convention);
        }

        return new SessionStandings
        {
            SessionId = sessionId,
            CapturedAtUtc = capturedAtUtc,
            SimulationTime = raw.Player.GameSimulationTime,
            // Derived exactly like SessionInfo.SimDriverId so the local car matches its own row in
            // Drivers by the same key every other machine will use for it.
            LocalSimDriverId = (NullIfNotPositive(raw.VehicleInfo.UserId) ?? NullIfNotPositive(raw.Player.UserId))?.ToString(),
            Drivers = standings,
        };
    }

    /// <summary>Builds the canonical lap record for a lap that just completed.</summary>
    /// <param name="raw">The snapshot in which the lap counter was observed to have advanced.</param>
    /// <param name="sessionId">The session the lap belongs to.</param>
    /// <param name="completedLapNumber">The 1-indexed number of the lap that just completed.</param>
    /// <param name="snapshotDescribesThisLap">
    /// Whether <paramref name="raw"/>'s lap-scoped fields (<c>lap_time_previous_self</c>,
    /// <c>prev_lap_valid</c>, ...) actually describe lap <paramref name="completedLapNumber"/>.
    /// They only ever describe the <i>most recently</i> completed lap, so when a poll is missed and
    /// the counter jumps by more than one, the earlier laps in that jump must be reported with
    /// their timings unknown rather than with the last lap's numbers copied onto them.
    /// </param>
    public static LapInfo ToLapInfo(in R3ESharedRaw raw, Guid sessionId, int completedLapNumber, bool snapshotDescribesThisLap = true)
    {
        if (!snapshotDescribesThisLap)
        {
            return new LapInfo
            {
                SessionId = sessionId,
                LapNumber = completedLapNumber,
                LapTime = null,
                FuelUsed = null,
                AverageSpeed = null,
                MaxSpeed = null,
                // Unknown, and LapInfo.IsValid has no third state — false is the safe reading,
                // since treating an unverifiable lap as valid would let it into analysis.
                IsValid = false,
            };
        }

        return new LapInfo
        {
            SessionId = sessionId,
            LapNumber = completedLapNumber,
            LapTime = raw.LapTimePreviousSelf < 0f ? null : TimeSpan.FromSeconds(raw.LapTimePreviousSelf),
            // fuel_per_lap is documented as "estimation when not enough data, then max recorded
            // fuel per lap" — the closest available field to "fuel used this lap", not an exact
            // per-lap actual; the connector does no analysis so it is passed through as-is.
            FuelUsed = NullIfNegative(raw.FuelPerLap),
            AverageSpeed = null, // Not exposed by the shared memory API.
            MaxSpeed = null, // Not exposed by the shared memory API.
            IsValid = raw.PrevLapValid == 1,
        };
    }

    /// <summary>
    /// Hand-written, curated subset of the raw fields that have no canonical equivalent. This is
    /// intentionally not a reflective dump of the whole struct: that would be far too slow at a
    /// 60 Hz poll rate and would leak reserved/padding fields that carry no meaning.
    /// </summary>
    private static string BuildSampleExtras(in R3ESharedRaw raw)
    {
        var (buffer, writer) = RentExtrasWriter();
        WriteSampleExtras(writer, in raw);
        return MaterializeText(buffer, writer);
    }

    private static void WriteSampleExtras(Utf8JsonWriter writer, in R3ESharedRaw raw)
    {
        writer.WriteStartObject();

        writer.WriteStartObject("pushToPass");
        writer.WriteNumber("available", raw.PushToPass.Available);
        writer.WriteNumber("engaged", raw.PushToPass.Engaged);
        writer.WriteNumber("amountLeft", raw.PushToPass.AmountLeft);
        writer.WriteNumber("engagedTimeLeftSeconds", raw.PushToPass.EngagedTimeLeft);
        writer.WriteNumber("waitTimeLeftSeconds", raw.PushToPass.WaitTimeLeft);
        writer.WriteEndObject();

        writer.WriteStartObject("drs");
        writer.WriteNumber("equipped", raw.Drs.Equipped);
        writer.WriteNumber("available", raw.Drs.Available);
        writer.WriteNumber("numActivationsLeft", raw.Drs.NumActivationsLeft);
        writer.WriteNumber("engaged", raw.Drs.Engaged);
        writer.WriteNumber("numActivationsTotal", raw.DrsNumActivationsTotal);
        writer.WriteEndObject();

        writer.WriteStartObject("damage");
        writer.WriteNumber("engine", raw.CarDamage.Engine);
        writer.WriteNumber("transmission", raw.CarDamage.Transmission);
        writer.WriteNumber("aerodynamics", raw.CarDamage.Aerodynamics);
        writer.WriteNumber("suspension", raw.CarDamage.Suspension);
        writer.WriteEndObject();

        writer.WriteStartArray("brakeTemperatureCelsius");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.BrakeTemp[i].CurrentTemp);
        }
        writer.WriteEndArray();

        writer.WriteStartArray("brakePressureKiloNewtons");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.BrakePressure[i]);
        }
        writer.WriteEndArray();

        // Per-tyre channels with no canonical equivalent yet. tyreGrip is the reason this block
        // exists: it is grip loss measured directly, rather than inferred from lap time the way a
        // lap-time trend has to, and a degradation model built without it can only see the symptom.
        //
        // Written raw, sentinels included (-1 = N/A), like every other value in this object. These
        // are candidates for promotion to canonical fields, and that is where the -1 -> null
        // translation belongs, alongside the one TyreWear already gets.
        writer.WriteStartArray("tyreGrip");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.TireGrip[i]);
        }
        writer.WriteEndArray();

        writer.WriteStartArray("tyreLoadNewtons");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.TireLoad[i]);
        }
        writer.WriteEndArray();

        writer.WriteStartArray("tyreDirt");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.TireDirt[i]);
        }
        writer.WriteEndArray();

        writer.WriteStartArray("tyreFlatspot");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.TireFlatspot[i]);
        }
        writer.WriteEndArray();

        // Rotation speed in rad/s alongside the canonical linear WheelSpeed: the pair is what makes
        // slip ratio recoverable later, which neither value gives on its own.
        writer.WriteStartArray("tyreRotationRadiansPerSecond");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.TireRps[i]);
        }
        writer.WriteEndArray();

        // Surface material under each tyre (r3e_mtrl_type) — the cheapest evidence that a lap left
        // the track, which is what lap quality scoring needs to discount it.
        writer.WriteStartArray("tyreSurfaceMaterial");
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(raw.TireOnMtrl[i]);
        }
        writer.WriteEndArray();

        writer.WriteNumber("batteryStateOfChargePercent", raw.BatterySoC);
        writer.WriteNumber("virtualEnergyLeftMj", raw.VirtualEnergyLeft);
        writer.WriteNumber("virtualEnergyCapacityMj", raw.VirtualEnergyCapacity);
        writer.WriteNumber("virtualEnergyPerLapMj", raw.VirtualEnergyPerLap);

        writer.WriteNumber("engineTempCelsius", raw.EngineTemp);
        writer.WriteNumber("engineOilTempCelsius", raw.EngineOilTemp);
        writer.WriteNumber("fuelPressureKpa", raw.FuelPressure);
        writer.WriteNumber("engineOilPressureKpa", raw.EngineOilPressure);
        writer.WriteNumber("turboPressureBar", raw.TurboPressure);

        writer.WriteNumber("tractionControlSetting", raw.TractionControlSetting);
        writer.WriteNumber("tractionControlPercent", raw.TractionControlPercent);
        writer.WriteNumber("engineMapSetting", raw.EngineMapSetting);
        writer.WriteNumber("engineBrakeSetting", raw.EngineBrakeSetting);
        writer.WriteNumber("absSetting", raw.AbsSetting);

        writer.WriteNumber("tireTypeFront", raw.TireTypeFront);
        writer.WriteNumber("tireTypeRear", raw.TireTypeRear);
        writer.WriteNumber("tireSubtypeFront", raw.TireSubtypeFront);
        writer.WriteNumber("tireSubtypeRear", raw.TireSubtypeRear);

        writer.WriteNumber("controlType", raw.ControlType);

        writer.WriteStartObject("flags");
        writer.WriteNumber("yellow", raw.Flags.Yellow);
        writer.WriteNumber("blue", raw.Flags.Blue);
        writer.WriteNumber("black", raw.Flags.Black);
        writer.WriteNumber("green", raw.Flags.Green);
        writer.WriteNumber("checkered", raw.Flags.Checkered);
        writer.WriteNumber("white", raw.Flags.White);
        writer.WriteNumber("blackAndWhite", raw.Flags.BlackAndWhite);
        writer.WriteEndObject();

        writer.WriteStartObject("pit");
        writer.WriteNumber("windowStatus", raw.PitWindowStatus);
        writer.WriteNumber("windowStart", raw.PitWindowStart);
        writer.WriteNumber("windowEnd", raw.PitWindowEnd);
        writer.WriteNumber("state", raw.PitState);
        writer.WriteNumber("action", raw.PitAction);
        writer.WriteNumber("numPitstopsPerformed", raw.NumPitstopsPerformed);
        writer.WriteNumber("totalDurationSeconds", raw.PitTotalDuration);
        writer.WriteNumber("elapsedTimeSeconds", raw.PitElapsedTime);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    /// <summary>
    /// Hand-written, curated subset of session-level raw fields that have no canonical
    /// equivalent (mirrors <see cref="BuildSampleExtras"/> but only runs once per session).
    /// </summary>
    private static JsonElement BuildSessionExtras(in R3ESharedRaw raw)
    {
        var (buffer, writer) = RentExtrasWriter();
        WriteSessionExtras(writer, in raw);
        return MaterializeElement(buffer, writer);
    }

    private static void WriteSessionExtras(Utf8JsonWriter writer, in R3ESharedRaw raw)
    {
        writer.WriteStartObject();

        writer.WriteNumber("gameMode", raw.GameMode);
        writer.WriteNumber("sessionIteration", raw.SessionIteration);
        writer.WriteNumber("sessionLengthFormat", raw.SessionLengthFormat);
        writer.WriteNumber("numberOfLaps", raw.NumberOfLaps);
        writer.WriteNumber("sessionTimeDurationSeconds", raw.SessionTimeDuration);
        writer.WriteNumber("pitWindowStart", raw.PitWindowStart);
        writer.WriteNumber("pitWindowEnd", raw.PitWindowEnd);
        writer.WriteNumber("tireWearActive", raw.TireWearActive);
        writer.WriteNumber("fuelUseActive", raw.FuelUseActive);
        writer.WriteNumber("maxIncidentPoints", raw.MaxIncidentPoints);
        writer.WriteNumber("controlType", raw.ControlType);

        writer.WriteStartObject("vehicle");
        writer.WriteNumber("carNumber", raw.VehicleInfo.CarNumber);
        writer.WriteNumber("classId", raw.VehicleInfo.ClassId);
        writer.WriteNumber("modelId", raw.VehicleInfo.ModelId);
        writer.WriteNumber("userId", raw.VehicleInfo.UserId);
        writer.WriteNumber("slotId", raw.VehicleInfo.SlotId);
        writer.WriteNumber("teamId", raw.VehicleInfo.TeamId);
        writer.WriteNumber("liveryId", raw.VehicleInfo.LiveryId);
        writer.WriteNumber("manufacturerId", raw.VehicleInfo.ManufacturerId);
        writer.WriteNumber("engineType", raw.VehicleInfo.EngineType);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    // One scratch buffer and one JSON writer per thread, reused for every sample. Before, each of
    // the 60 samples per second allocated a fresh 1 KB ArrayBufferWriter and a fresh Utf8JsonWriter
    // purely to throw them away again. Utf8JsonWriter.Reset re-targets the existing instance, and
    // ResetWrittenCount rewinds the buffer, so the steady state allocates neither. Thread-static
    // rather than a shared instance because that needs no lock and no lifetime management; the
    // connector's poll loop is single-threaded, so in practice there is exactly one of each.
    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _extrasBuffer;

    [ThreadStatic]
    private static Utf8JsonWriter? _extrasWriter;

    private static (ArrayBufferWriter<byte> Buffer, Utf8JsonWriter Writer) RentExtrasWriter()
    {
        var buffer = _extrasBuffer ??= new ArrayBufferWriter<byte>(1024);
        buffer.ResetWrittenCount();

        var writer = _extrasWriter;
        if (writer is null)
        {
            writer = new Utf8JsonWriter(buffer);
            _extrasWriter = writer;
        }
        else
        {
            writer.Reset(buffer);
        }

        return (buffer, writer);
    }

    /// <summary>Turns what was just written into detached JSON text.</summary>
    /// <remarks>
    /// One UTF-8 decode and nothing else. The sample path runs at the poll rate, and its result is
    /// carried as text the whole way to the <c>jsonb</c> column, so there is nothing to parse here.
    /// </remarks>
    private static string MaterializeText(ArrayBufferWriter<byte> buffer, Utf8JsonWriter writer)
    {
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Turns what was just written into a detached <see cref="JsonElement"/>.</summary>
    /// <remarks>
    /// Session extras are still a <see cref="JsonElement"/> on <c>SessionInfo</c>, and a
    /// <see cref="JsonElement"/> is only valid while its backing <see cref="JsonDocument"/> lives —
    /// the clone is what detaches it from this reused buffer. Affordable here because it runs once
    /// per session rather than once per sample.
    /// </remarks>
    private static JsonElement MaterializeElement(ArrayBufferWriter<byte> buffer, Utf8JsonWriter writer)
    {
        writer.Flush();
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}

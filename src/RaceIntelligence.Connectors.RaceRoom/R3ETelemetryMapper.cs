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
/// canonical telemetry model. This is a pure, allocation-light translation layer — it performs no
/// analysis, matching the collector design principle that raw telemetry collection and analysis
/// are separate concerns.
/// </summary>
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
            NullIfNegative(raw.TireWear[0]),
            NullIfNegative(raw.TireWear[1]),
            NullIfNegative(raw.TireWear[2]),
            NullIfNegative(raw.TireWear[3]));

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
            Gear = raw.Gear, // -2 = N/A, -1 = reverse, 0 = neutral, already the canonical convention.
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

    /// <summary>Builds the canonical lap record for a lap that just completed.</summary>
    /// <param name="completedLapNumber">The 1-indexed number of the lap that just completed.</param>
    public static LapInfo ToLapInfo(in R3ESharedRaw raw, Guid sessionId, int completedLapNumber)
    {
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
    private static JsonElement BuildSampleExtras(in R3ESharedRaw raw)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer))
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

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Hand-written, curated subset of session-level raw fields that have no canonical
    /// equivalent (mirrors <see cref="BuildSampleExtras"/> but only runs once per session).
    /// </summary>
    private static JsonElement BuildSessionExtras(in R3ESharedRaw raw)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
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

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}

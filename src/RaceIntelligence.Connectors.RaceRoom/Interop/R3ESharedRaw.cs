using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Byte-exact mirror of RaceRoom's <c>r3e_shared</c> struct (shared memory name <c>"$R3E"</c>),
/// ported field-for-field from the authoritative
/// <see href="https://github.com/kwstudios-sweden/r3e-api">r3e-api</see> C header (<c>sample-c/src/r3e.h</c>,
/// API version 3.5), cross-checked against the official C# port (<c>sample-csharp/src/R3E.cs</c>).
/// </summary>
/// <remarks>
/// <para>
/// The header wraps every struct in <c>#pragma pack(push, 1)</c> — there is no compiler padding
/// anywhere in the real layout, so every struct here (this one and every nested one under
/// <c>Interop/</c>) must carry <c>[StructLayout(LayoutKind.Sequential, Pack = 1)]</c>. Getting a
/// single field's type or position wrong silently shifts every field that follows it; see
/// <c>R3ESharedRawLayoutTests</c> for the golden size/offset assertions that are the only
/// automated defense against that.
/// </para>
/// <para>
/// Reserved <c>unused*</c> fields are kept even though nothing reads them — deleting one would
/// silently corrupt the offset of every field after it.
/// </para>
/// <para>
/// The official r3e-api samples do not version-check the shared memory before reading it — this
/// connector does (see <see cref="RaceIntelligence.Connectors.RaceRoom.Interop.R3EVersionGate"/>),
/// because trusting an unfamiliar major/minor version to still match this exact layout would risk
/// silently misinterpreting every field.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3ESharedRaw
{
    //////////////////////////////////////////////////////////////////////
    // Version
    //////////////////////////////////////////////////////////////////////

    /// <summary>Offset 0. Must equal <see cref="R3EVersionGate.RequiredMajor"/> (3) — layout is not guaranteed across majors.</summary>
    public int VersionMajor;

    /// <summary>Offset 4.</summary>
    public int VersionMinor;

    /// <summary>Byte offset from the start of this struct to <see cref="NumCars"/>.</summary>
    public int AllDriversOffset;

    /// <summary>Size, in bytes, of the <see cref="R3EDriverData"/> struct as reported by the game.</summary>
    public int DriverDataSize;

    //////////////////////////////////////////////////////////////////////
    // Game state
    //////////////////////////////////////////////////////////////////////

    /// <summary>See <c>r3e_gamemode</c> (track test, single race, championship, multiplayer, ...).</summary>
    public int GameMode;

    public int GamePaused;

    /// <summary>0 = in-session/on-track, 1 = in menus. Drives the Connected/InSession boundary.</summary>
    public int GameInMenus;

    public int GameInReplay;
    public int GameUsingVr;
    public int GamePlayerInGarage;

    //////////////////////////////////////////////////////////////////////
    // High detail
    //////////////////////////////////////////////////////////////////////

    /// <summary>High-precision data for the player's vehicle only. Not valid for AI/remote/replay.</summary>
    public R3EPlayerData Player;

    //////////////////////////////////////////////////////////////////////
    // Event and session
    //////////////////////////////////////////////////////////////////////

    /// <summary>UTF-8, NUL-terminated.</summary>
    public Utf8Name64 TrackName;

    /// <summary>UTF-8, NUL-terminated.</summary>
    public Utf8Name64 LayoutName;

    public int TrackId;
    public int LayoutId;

    /// <summary>Unit: meters.</summary>
    public float LayoutLength;

    public R3ESectorStarts SectorStartFactors;

    /// <summary>Race session lap counts, index 0-2 = race 1-3. -1 = N/A.</summary>
    public Int3 RaceSessionLaps;

    /// <summary>Race session minute counts, index 0-2 = race 1-3. -1 = N/A. If both are set, minutes run first then laps are added.</summary>
    public Int3 RaceSessionMinutes;

    /// <summary>0-indexed championship event index. -1 = N/A.</summary>
    public int EventIndex;

    /// <summary>-1 = unavailable, 0 = practice, 1 = qualify, 2 = race, 3 = warmup (<c>r3e_session</c>).</summary>
    public int SessionType;

    /// <summary>1 = first iteration of this session type, 2 = second, etc. -1 = N/A.</summary>
    public int SessionIteration;

    /// <summary>-1 = N/A, 0 = time-based, 1 = lap-based, 2 = time+extra lap (<c>r3e_session_length_format</c>).</summary>
    public int SessionLengthFormat;

    /// <summary>Unit: m/s.</summary>
    public float SessionPitSpeedLimit;

    /// <summary>-1 = unavailable, 1 = garage, 2 = gridwalk, 3 = formation, 4 = countdown, 5 = green, 6 = checkered (<c>r3e_session_phase</c>).</summary>
    public int SessionPhase;

    /// <summary>-1 = unavailable, 0 = off, 1-5 = red lights counting down, 6 = green light on.</summary>
    public int StartLights;

    /// <summary>-1 = N/A, 0 = off, 1-4 = 1x-4x tyre wear rate.</summary>
    public int TireWearActive;

    /// <summary>-1 = N/A, 0 = off, 1-4 = 1x-4x fuel usage rate.</summary>
    public int FuelUseActive;

    /// <summary>Total laps in the race, or -1 if not in race mode.</summary>
    public int NumberOfLaps;

    /// <summary>Unit: seconds. -1.0 = N/A (only meaningful in time-based sessions).</summary>
    public float SessionTimeDuration;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float SessionTimeRemaining;

    /// <summary>Server max incident points. -1 = N/A.</summary>
    public int MaxIncidentPoints;

    /// <summary>Reserved data — DO NOT remove; required to keep every following field's offset correct.</summary>
    public float EventUnused1;

    /// <summary>Reserved data — DO NOT remove; required to keep every following field's offset correct.</summary>
    public float EventUnused2;

    //////////////////////////////////////////////////////////////////////
    // Pit
    //////////////////////////////////////////////////////////////////////

    /// <summary>-1 = N/A, 0 = disabled, 1 = closed, 2 = open, 3 = stopped, 4 = completed (<c>r3e_pit_window</c>).</summary>
    public int PitWindowStatus;

    /// <summary>Minute/lap the pit window opens. -1 = N/A. Unit: minutes in time-based sessions, otherwise lap.</summary>
    public int PitWindowStart;

    /// <summary>Minute/lap the pit window must be used by. -1 = N/A. Unit: minutes in time-based sessions, otherwise lap.</summary>
    public int PitWindowEnd;

    /// <summary>-1 = N/A, whether the current vehicle is in the pitlane.</summary>
    public int InPitlane;

    /// <summary>Currently selected pit menu entry (<c>r3e_pit_menu_selection</c>).</summary>
    public int PitMenuSelection;

    /// <summary>State per pit menu entry (preset/buttons: -1 = not selectable, 1 = selectable; actions: -1 = N/A, 0 = unmarked, 1 = marked for fix).</summary>
    public PitMenuState12 PitMenuState;

    /// <summary>-1 = N/A, 0 = none, 1 = requested stop, 2 = entering pitlane, 3 = stopped at pitspot, 4 = exiting.</summary>
    public int PitState;

    public float PitTotalDuration;
    public float PitElapsedTime;

    /// <summary>-1 = N/A, 0 = none, 1 = preparing, otherwise a bitmask of actions (see r3e.h).</summary>
    public int PitAction;

    /// <summary>Pitstops the current vehicle has performed. -1 = N/A.</summary>
    public int NumPitstopsPerformed;

    /// <summary>-1.0 = N/A, else seconds.</summary>
    public float PitMinDurationTotal;

    /// <summary>-1.0 = N/A, else seconds.</summary>
    public float PitMinDurationLeft;

    //////////////////////////////////////////////////////////////////////
    // Scoring & timings
    //////////////////////////////////////////////////////////////////////

    public R3EFlags Flags;

    /// <summary>Current race position, 1 = first place.</summary>
    public int Position;

    /// <summary>Position within the car's class, based on performance index.</summary>
    public int PositionClass;

    /// <summary>-1 = N/A, 0 = none, 1 = finished, 2 = DNF, 3 = DNQ, 4 = DNS, 5 = DQ (<c>r3e_finish_status</c>).</summary>
    public int FinishStatus;

    /// <summary>Total cut-track warnings. -1 = N/A.</summary>
    public int CutTrackWarnings;

    /// <summary>Pending penalties by type for the car. -1 = N/A.</summary>
    public R3ECutTrackPenalties Penalties;

    /// <summary>Total pending penalties for the car.</summary>
    public int NumPenalties;

    /// <summary>Laps completed. 6 means the car is on its 7th lap. -1 = N/A.</summary>
    public int CompletedLaps;

    public int CurrentLapValid;
    public int TrackSector;
    public float LapDistance;

    /// <summary>Fraction of lap completed, 0.0-1.0. -1.0 = N/A.</summary>
    public float LapDistanceFraction;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float LapTimeBestLeader;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float LapTimeBestLeaderClass;

    /// <summary>Fastest lap's sector times by anyone in session. Unit: seconds. -1.0 = N/A.</summary>
    public Float3 SectorTimesSessionBestLap;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float LapTimeBestSelf;

    public Float3 SectorTimesBestSelf;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float LapTimePreviousSelf;

    public Float3 SectorTimesPreviousSelf;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float LapTimeCurrentSelf;

    public Float3 SectorTimesCurrentSelf;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float LapTimeDeltaLeader;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float LapTimeDeltaLeaderClass;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float TimeDeltaFront;

    /// <summary>Unit: seconds. -1.0 = N/A.</summary>
    public float TimeDeltaBehind;

    /// <summary>Delta between this car's current and best lap. Unit: seconds. -1000.0 = N/A.</summary>
    public float TimeDeltaBestSelf;

    /// <summary>Best time for each sector no matter the lap. Unit: seconds. -1.0 = N/A.</summary>
    public Float3 BestIndividualSectorTimeSelf;

    public Float3 BestIndividualSectorTimeLeader;
    public Float3 BestIndividualSectorTimeLeaderClass;

    /// <summary>-1 = N/A.</summary>
    public int IncidentPoints;

    /// <summary>-1 = N/A, 0 = this+next lap valid, 1 = this lap invalid, 2 = this+next lap invalid.</summary>
    public int LapValidState;

    /// <summary>-1 = N/A, 0 = invalid, 1 = valid. Refers to the lap that just completed.</summary>
    public int PrevLapValid;

    /// <summary>-1.0 = N/A, 0.0-1.0.</summary>
    public float DischargeRate;

    /// <summary>-1.0 = N/A, 0.0-1.0.</summary>
    public float BrakeRegen;

    /// <summary>Reserved data — DO NOT remove; required to keep every following field's offset correct.</summary>
    public float Unused1;

    //////////////////////////////////////////////////////////////////////
    // Vehicle information
    //////////////////////////////////////////////////////////////////////

    /// <summary>Ids only — no human-readable car/class/manufacturer names are exposed over shared memory.</summary>
    public R3EDriverInfo VehicleInfo;

    /// <summary>UTF-8, NUL-terminated.</summary>
    public Utf8Name64 PlayerName;

    //////////////////////////////////////////////////////////////////////
    // Vehicle state
    //////////////////////////////////////////////////////////////////////

    /// <summary>-1 = unavailable, 0 = player, 1 = AI, 2 = remote, 3 = replay (<c>r3e_control</c>).</summary>
    public int ControlType;

    /// <summary>Unit: m/s.</summary>
    public float CarSpeed;

    /// <summary>Unit: rad/s. NOT rpm — convert via <c>rps * 60 / (2*pi)</c>.</summary>
    public float EngineRps;

    public float MaxEngineRps;
    public float UpshiftRps;

    /// <summary>-2 = N/A, -1 = reverse, 0 = neutral, 1+ = forward gear (electric cars use 2 for regen braking).</summary>
    public int Gear;

    /// <summary>-1 = N/A.</summary>
    public int NumGears;

    /// <summary>Center of gravity in world space (X, Y, Z), Y = up.</summary>
    public R3EVec3F32 CarCgLocation;

    /// <summary>Pitch, yaw, roll. Unit: radians.</summary>
    public R3EOriF32 CarOrientation;

    /// <summary>Local-space acceleration; from car center +X=left, +Y=up, +Z=back. Unit: m/s^2.</summary>
    public R3EVec3F32 LocalAcceleration;

    /// <summary>Car + penalty weight + fuel. Unit: kg.</summary>
    public float TotalMass;

    /// <summary>Unit: liters. Not valid for remote players.</summary>
    public float FuelLeft;

    public float FuelCapacity;

    /// <summary>Estimated when not enough data yet, then max recorded fuel per lap. Not valid for remote players.</summary>
    public float FuelPerLap;

    /// <summary>Unit: MJ. -1.0 = not enough data yet. Not valid for remote players.</summary>
    public float VirtualEnergyLeft;

    public float VirtualEnergyCapacity;
    public float VirtualEnergyPerLap;

    /// <summary>Unit: Celsius. Not valid for AI/remote players.</summary>
    public float EngineTemp;

    public float EngineOilTemp;

    /// <summary>Unit: kPa. Not valid for AI/remote players.</summary>
    public float FuelPressure;

    public float EngineOilPressure;

    /// <summary>Unit: bar. -1.0 = N/A. Not valid for AI/remote players.</summary>
    public float TurboPressure;

    /// <summary>Range 0.0-1.0. -1.0 = N/A. Not valid for AI/remote players.</summary>
    public float Throttle;

    public float ThrottleRaw;

    /// <summary>Range 0.0-1.0. -1.0 = N/A. Not valid for AI/remote players.</summary>
    public float Brake;

    public float BrakeRaw;

    /// <summary>Range 0.0-1.0. -1.0 = N/A. Not valid for AI/remote players.</summary>
    public float Clutch;

    public float ClutchRaw;

    /// <summary>Range -1.0 to 1.0 (legitimately negative — NOT an N/A sentinel). Not valid for AI/remote players.</summary>
    public float SteerInputRaw;

    /// <summary>Degrees, center to full lock. Not valid for AI/remote players.</summary>
    public int SteerLockDegrees;

    /// <summary>Degrees, full left to full right. Not valid for AI/remote players.</summary>
    public int SteerWheelRangeDegrees;

    public R3EAidSettings AidSettings;
    public R3EDrs Drs;

    /// <summary>-1 = N/A, 0 = inactive, 1 = active.</summary>
    public int PitLimiter;

    public R3EPushToPass PushToPass;

    /// <summary>Fraction towards the back wheels (0.3 = 30%). -1.0 = N/A. Not valid for AI/remote players.</summary>
    public float BrakeBias;

    /// <summary>Total DRS activations available. -1 = N/A or endless. Kept outside <see cref="Drs"/> for backwards compatibility.</summary>
    public int DrsNumActivationsTotal;

    /// <summary>Total PTP activations available. -1 = N/A, unrestricted, or endless. Kept outside <see cref="PushToPass"/> for backwards compatibility.</summary>
    public int PtpNumActivationsTotal;

    /// <summary>Range 0.0-100.0. -1.0 = N/A.</summary>
    public float BatterySoC;

    /// <summary>Brake water tank. Unit: liters. -1.0 = N/A.</summary>
    public float WaterLeft;

    /// <summary>-1 = N/A.</summary>
    public int AbsSetting;

    /// <summary>-1 = N/A or doesn't exist, 0 = off, 1 = on, 2 = strobing.</summary>
    public int HeadLights;

    /// <summary>-1 = N/A, 0 = auto, 180-1800 = manual. Not valid for AI/remote players.</summary>
    public int SteerWheelMaxRotation;

    //////////////////////////////////////////////////////////////////////
    // Tires
    //////////////////////////////////////////////////////////////////////

    /// <summary>Deprecated — use <see cref="TireTypeFront"/>/<see cref="TireTypeRear"/> instead (<c>r3e_tire_type</c>).</summary>
    public int TireType;

    /// <summary>Rotation speed per tyre (FL, FR, RL, RR). Unit: rad/s.</summary>
    public Float4 TireRps;

    /// <summary>Wheel speed per tyre (FL, FR, RL, RR). Unit: m/s.</summary>
    public Float4 TireSpeed;

    /// <summary>Range 0.0-1.0. -1.0 = N/A.</summary>
    public Float4 TireGrip;

    /// <summary>Range 0.0-1.0. -1.0 = N/A.</summary>
    public Float4 TireWear;

    /// <summary>-1 = N/A, 0 = false, 1 = true.</summary>
    public Int4 TireFlatspot;

    /// <summary>Unit: kPa. -1.0 = N/A. Not valid for AI/remote players.</summary>
    public Float4 TirePressure;

    /// <summary>Percentage of dirt on tyre, 0.0-1.0. -1.0 = N/A.</summary>
    public Float4 TireDirt;

    /// <summary>Tread temperature per tyre (FL, FR, RL, RR). Not valid for AI/remote players.</summary>
    public TireTemp4 TireTemp;

    /// <summary>Which tyre type the front tyres have (<c>r3e_tire_type</c>).</summary>
    public int TireTypeFront;

    public int TireTypeRear;

    /// <summary>Which tyre subtype the front tyres have (<c>r3e_tire_subtype</c>).</summary>
    public int TireSubtypeFront;

    public int TireSubtypeRear;

    /// <summary>Brake temperature per tyre (FL, FR, RL, RR). Not valid for AI/remote players.</summary>
    public BrakeTemp4 BrakeTemp;

    /// <summary>Unit: kN. -1.0 = N/A. Not valid for AI/remote players.</summary>
    public Float4 BrakePressure;

    //////////////////////////////////////////////////////////////////////
    // Electronics
    //////////////////////////////////////////////////////////////////////

    /// <summary>-1 = N/A.</summary>
    public int TractionControlSetting;

    public int EngineMapSetting;
    public int EngineBrakeSetting;

    /// <summary>Range 0.0-100.0 percent. -1.0 = N/A.</summary>
    public float TractionControlPercent;

    /// <summary>Surface material under each tyre (<c>r3e_mtrl_type</c>).</summary>
    public Int4 TireOnMtrl;

    /// <summary>Unit: N. -1.0 = N/A.</summary>
    public Float4 TireLoad;

    //////////////////////////////////////////////////////////////////////
    // Damage
    //////////////////////////////////////////////////////////////////////

    /// <summary>Not valid for AI/remote players.</summary>
    public R3ECarDamage CarDamage;

    //////////////////////////////////////////////////////////////////////
    // Driver info
    //////////////////////////////////////////////////////////////////////

    /// <summary>Number of cars (including the player) in the race. <see cref="AllDriversOffset"/> points here.</summary>
    public int NumCars;

    // The header's final field, all_drivers_data_1, is an R3E_NUM_DRIVERS_MAX (128) entry
    // R3EDriverData array — 41,984 of the block's 43,996 bytes. It is deliberately NOT mirrored
    // here: nothing in this connector reads a single field of it, and every poll would otherwise
    // copy it out of the mapping, ~2.5 MB/s of pure waste at 60 Hz. Being the *last* field, leaving
    // it out shifts no other field's offset, and the version gate only requires the mapping to be
    // at least this struct's size, so a shorter read of a longer block is exactly what we want.
    // See R3ESharedRawLayoutTests, which pins both this struct's size and the full published block
    // size so the omission stays deliberate rather than becoming a forgotten transcription gap.
}

using System.Runtime.InteropServices;
using System.Text;
using RaceIntelligence.Connectors.RaceRoom.Interop;

namespace RaceIntelligence.Connectors.RaceRoom.Tests.Support;

/// <summary>
/// Fluent builder for a plausible, fully-populated <see cref="R3ESharedRaw"/> snapshot in managed
/// memory, for use with <see cref="FakeSharedMemoryView"/>. Defaults to a sane idle/in-menus
/// state with a valid version header (major 3, minor 5) and every documented "-1 = N/A" sentinel
/// field set to its N/A value, so tests only need to override the handful of fields they actually
/// care about.
/// </summary>
internal sealed class R3ESharedRawBuilder
{
    private R3ESharedRaw _raw = CreateDefault();
    private R3EDriverData[] _drivers = [];

    private static R3ESharedRaw CreateDefault()
    {
        R3ESharedRaw raw = default;

        raw.VersionMajor = R3EVersionGate.RequiredMajor;
        raw.VersionMinor = R3EVersionGate.MinimumMinor;

        // A real block describes its own layout in the header, and R3EVersionGate checks that
        // description rather than trusting the minor version. Defaulting these to the layout this
        // connector compiles to is what makes a builder-produced block look like a matching game
        // build; tests that want a mismatched or silent one override them via WithLayoutSelfDescription.
        raw.AllDriversOffset = R3EVersionGate.ExpectedAllDriversOffset;
        raw.DriverDataSize = R3EVersionGate.ExpectedDriverDataSize;

        raw.GameInMenus = 1;
        raw.GamePaused = 0;
        raw.GameInReplay = 0;
        raw.GameUsingVr = 0;
        raw.GamePlayerInGarage = 1;

        raw.SessionType = (int)R3ESessionType.Unavailable;
        raw.SessionPhase = (int)R3ESessionPhase.Unavailable;
        raw.SessionIteration = -1;
        raw.SessionLengthFormat = -1;
        raw.NumberOfLaps = -1;
        raw.SessionTimeDuration = -1f;
        raw.SessionTimeRemaining = -1f;
        raw.MaxIncidentPoints = -1;

        raw.PitWindowStatus = -1;
        raw.PitWindowStart = -1;
        raw.PitWindowEnd = -1;
        raw.InPitlane = -1;
        raw.PitState = -1;
        raw.PitAction = -1;
        raw.NumPitstopsPerformed = -1;
        raw.PitMinDurationTotal = -1f;
        raw.PitMinDurationLeft = -1f;

        raw.Position = 0;
        raw.PositionClass = 0;
        raw.FinishStatus = -1;
        raw.CutTrackWarnings = -1;
        raw.CompletedLaps = -1;
        raw.TrackSector = 0;
        raw.LapDistanceFraction = -1f;
        raw.LapTimeBestLeader = -1f;
        raw.LapTimeBestLeaderClass = -1f;
        raw.LapTimeBestSelf = -1f;
        raw.LapTimePreviousSelf = -1f;
        raw.LapTimeCurrentSelf = -1f;
        raw.LapTimeDeltaLeader = -1f;
        raw.LapTimeDeltaLeaderClass = -1f;
        raw.TimeDeltaFront = -1f;
        raw.TimeDeltaBehind = -1f;
        raw.TimeDeltaBestSelf = -1000f;
        raw.IncidentPoints = -1;
        raw.LapValidState = -1;
        raw.PrevLapValid = -1;
        raw.DischargeRate = -1f;
        raw.BrakeRegen = -1f;

        raw.ControlType = (int)R3EControlType.Player;
        raw.EngineRps = 0f;
        raw.Gear = -2;
        raw.NumGears = -1;
        raw.FuelLeft = 0f;
        raw.FuelPerLap = -1f;
        raw.VirtualEnergyLeft = -1f;
        raw.Throttle = -1f;
        raw.Brake = -1f;
        raw.Clutch = -1f;
        raw.SteerInputRaw = 0f;
        raw.PitLimiter = -1;
        raw.BrakeBias = -1f;
        raw.DrsNumActivationsTotal = -1;
        raw.PtpNumActivationsTotal = -1;
        raw.BatterySoC = -1f;
        raw.WaterLeft = -1f;
        raw.AbsSetting = -1;
        raw.HeadLights = -1;
        raw.SteerWheelMaxRotation = -1;

        SetAllFour(ref raw.TireGrip, -1f);
        SetAllFour(ref raw.TireWear, -1f);
        SetAllFour(ref raw.TirePressure, -1f);
        SetAllFour(ref raw.TireDirt, -1f);
        SetAllFour(ref raw.TireLoad, -1f);

        raw.TractionControlSetting = -1;
        raw.TractionControlPercent = -1f;

        raw.CarDamage.Engine = -1f;
        raw.CarDamage.Transmission = -1f;
        raw.CarDamage.Aerodynamics = -1f;
        raw.CarDamage.Suspension = -1f;

        return raw;
    }

    private static void SetAllFour(ref Float4 field, float value)
    {
        field[0] = value;
        field[1] = value;
        field[2] = value;
        field[3] = value;
    }

    /// <summary>Puts the snapshot back into a sane idle/in-menus state (the default).</summary>
    public R3ESharedRawBuilder InMenus()
    {
        _raw.GameInMenus = 1;
        _raw.SessionType = (int)R3ESessionType.Unavailable;
        _raw.SessionPhase = (int)R3ESessionPhase.Unavailable;
        return this;
    }

    /// <summary>
    /// Opens the in-session (ESC) menu over whatever session the builder currently describes,
    /// leaving the session fields untouched.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="InMenus"/>, which models the <i>main</i> menu and clears the
    /// session type and phase. RaceRoom sets <c>game_in_menus</c> for both, which is what made an
    /// ordinary pause indistinguishable from leaving the session.
    /// </remarks>
    public R3ESharedRawBuilder InSessionMenu()
    {
        _raw.GameInMenus = 1;
        return this;
    }

    /// <summary>Sets <c>game_paused</c> — a pause that does not necessarily open a menu.</summary>
    public R3ESharedRawBuilder Paused(bool paused = true)
    {
        _raw.GamePaused = paused ? 1 : 0;
        return this;
    }

    /// <summary>Sets <c>session_iteration</c>: 1 = first session of this type, 2 = second, -1 = N/A.</summary>
    public R3ESharedRawBuilder WithSessionIteration(int iteration)
    {
        _raw.SessionIteration = iteration;
        return this;
    }

    /// <summary>Puts the snapshot into an on-track race session at the given track/layout.</summary>
    public R3ESharedRawBuilder InRaceSession(string track, string layout)
    {
        _raw.GameInMenus = 0;
        _raw.GamePlayerInGarage = 0;
        _raw.TrackName = EncodeName(track);
        _raw.LayoutName = EncodeName(layout);
        _raw.SessionType = (int)R3ESessionType.Race;
        _raw.SessionPhase = (int)R3ESessionPhase.Green;
        _raw.CompletedLaps = 0;

        // A clean on-track session: prev_lap_valid = 1 ("the lap that just completed was valid").
        // The all-sentinels default leaves this at -1 (N/A), which ToLapInfo correctly reports as
        // invalid -- so every lap produced by a state-machine test used to be an invalid lap, and
        // no assertion noticed. Tests that specifically want an N/A or invalid previous lap set it
        // themselves via WithPreviousLap.
        _raw.PrevLapValid = 1;
        return this;
    }

    /// <summary>Sets the previous lap's reported time and validity (<c>lap_time_previous_self</c>/<c>prev_lap_valid</c>).</summary>
    /// <param name="lapTimeSeconds">Lap time in seconds, or <see langword="null"/> for RaceRoom's -1.0 "not available" sentinel.</param>
    /// <param name="prevLapValid">-1 = N/A, 0 = invalid, 1 = valid.</param>
    public R3ESharedRawBuilder WithPreviousLap(float? lapTimeSeconds, int prevLapValid)
    {
        _raw.LapTimePreviousSelf = lapTimeSeconds ?? -1f;
        _raw.PrevLapValid = prevLapValid;
        return this;
    }

    public R3ESharedRawBuilder WithSessionType(R3ESessionType sessionType)
    {
        _raw.SessionType = (int)sessionType;
        return this;
    }

    public R3ESharedRawBuilder WithSessionPhase(R3ESessionPhase sessionPhase)
    {
        _raw.SessionPhase = (int)sessionPhase;
        return this;
    }

    public R3ESharedRawBuilder WithTicks(int ticks)
    {
        _raw.Player.GameSimulationTicks = ticks;
        return this;
    }

    public R3ESharedRawBuilder WithSimulationTime(double seconds)
    {
        _raw.Player.GameSimulationTime = seconds;
        return this;
    }

    public R3ESharedRawBuilder WithSpeed(float metersPerSecond)
    {
        _raw.CarSpeed = metersPerSecond;
        return this;
    }

    public R3ESharedRawBuilder WithThrottle(float value)
    {
        _raw.Throttle = value;
        return this;
    }

    public R3ESharedRawBuilder WithBrake(float value)
    {
        _raw.Brake = value;
        return this;
    }

    public R3ESharedRawBuilder WithGear(int gear)
    {
        _raw.Gear = gear;
        return this;
    }

    public R3ESharedRawBuilder WithEngineRps(float radiansPerSecond)
    {
        _raw.EngineRps = radiansPerSecond;
        return this;
    }

    public R3ESharedRawBuilder WithFuel(float liters)
    {
        _raw.FuelLeft = liters;
        return this;
    }

    public R3ESharedRawBuilder WithCompletedLaps(int laps)
    {
        _raw.CompletedLaps = laps;
        return this;
    }

    public R3ESharedRawBuilder WithTyrePressures(float frontLeft, float frontRight, float rearLeft, float rearRight)
    {
        _raw.TirePressure[0] = frontLeft;
        _raw.TirePressure[1] = frontRight;
        _raw.TirePressure[2] = rearLeft;
        _raw.TirePressure[3] = rearRight;
        return this;
    }

    public R3ESharedRawBuilder WithTyreWear(float frontLeft, float frontRight, float rearLeft, float rearRight)
    {
        _raw.TireWear[0] = frontLeft;
        _raw.TireWear[1] = frontRight;
        _raw.TireWear[2] = rearLeft;
        _raw.TireWear[3] = rearRight;
        return this;
    }

    public R3ESharedRawBuilder WithVersion(int major, int minor)
    {
        _raw.VersionMajor = major;
        _raw.VersionMinor = minor;
        return this;
    }

    /// <summary>
    /// Overrides the header's layout self-description (<c>all_drivers_offset</c>/<c>driver_data_size</c>),
    /// which is what <see cref="R3EVersionGate"/> actually checks. Use a value other than the
    /// default to stand in for a game build whose layout has moved, or zero for one that reports
    /// no layout at all.
    /// </summary>
    public R3ESharedRawBuilder WithLayoutSelfDescription(int allDriversOffset, int driverDataSize)
    {
        _raw.AllDriversOffset = allDriversOffset;
        _raw.DriverDataSize = driverDataSize;
        return this;
    }

    public R3ESharedRawBuilder WithPlayerName(string name)
    {
        _raw.PlayerName = EncodeName(name);
        return this;
    }

    /// <summary>
    /// Escape hatch for fields with no dedicated fluent method: apply an arbitrary mutation to the
    /// in-progress raw struct directly (every field on <see cref="R3ESharedRaw"/> is public).
    /// </summary>
    public R3ESharedRawBuilder Configure(RawEditor edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit(ref _raw);
        return this;
    }

    /// <summary>A mutation applied directly to a builder's in-progress <see cref="R3ESharedRaw"/>.</summary>
    public delegate void RawEditor(ref R3ESharedRaw raw);

    /// <summary>
    /// Places <paramref name="drivers"/> in the block's trailing <c>all_drivers_data_1</c> array and
    /// sets <c>num_cars</c> to match.
    /// </summary>
    /// <remarks>
    /// Only meaningful via <see cref="BuildBytes"/>: the array is not a field of
    /// <see cref="R3ESharedRaw"/> (it is deliberately unmapped), so <see cref="Build"/> cannot carry
    /// it. Overrides <see cref="WithCompletedLaps"/>-style scalar setters not at all — the two
    /// describe different cars' worth of state.
    /// </remarks>
    public R3ESharedRawBuilder WithDrivers(params R3EDriverData[] drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        _drivers = drivers;
        _raw.NumCars = drivers.Length;
        return this;
    }

    /// <summary>
    /// Sets <c>num_cars</c> without supplying matching entries, for tests that need the header to
    /// claim a field the block does not actually contain.
    /// </summary>
    public R3ESharedRawBuilder WithNumCars(int numCars)
    {
        _raw.NumCars = numCars;
        return this;
    }

    public R3ESharedRaw Build() => _raw;

    /// <summary>
    /// Builds and serializes directly to the byte layout <see cref="FakeSharedMemoryView"/> expects.
    /// </summary>
    /// <remarks>
    /// When <see cref="WithDrivers"/> supplied entries, the result is the mapped struct followed by
    /// those entries — which is where the real block puts them, since <c>all_drivers_offset</c>
    /// points at <c>num_cars</c> and the struct ends immediately after it. Without them the result
    /// is just the struct, exactly as before, so every existing test is unaffected.
    /// </remarks>
    public byte[] BuildBytes()
    {
        byte[] prefix = _raw.ToBytes();
        if (_drivers.Length == 0)
        {
            return prefix;
        }

        ReadOnlySpan<byte> driverBytes = MemoryMarshal.AsBytes<R3EDriverData>(_drivers);
        byte[] block = new byte[prefix.Length + driverBytes.Length];
        prefix.CopyTo(block, 0);
        driverBytes.CopyTo(block.AsSpan(prefix.Length));
        return block;
    }

    /// <summary>
    /// Encodes a UTF-8, NUL-terminated 64-byte name buffer, matching how RaceRoom's shared memory
    /// stores <c>track_name</c>/<c>layout_name</c>/<c>player_name</c>. The remaining bytes stay
    /// zero (NUL padding), which is also what makes an unset name decode as empty.
    /// </summary>
    internal static Utf8Name64 EncodeName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Utf8Name64 name = default;
        Span<byte> destination = name;
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > destination.Length)
        {
            throw new ArgumentException(
                $"'{value}' encodes to {byteCount} UTF-8 bytes, which does not fit in a {destination.Length}-byte name buffer.",
                nameof(value));
        }

        Encoding.UTF8.GetBytes(value, destination);
        return name;
    }

}

/// <summary>Converts a built <see cref="R3ESharedRaw"/> into the byte layout <see cref="FakeSharedMemoryView"/> expects.</summary>
internal static class R3ESharedRawSerialization
{
    internal static byte[] ToBytes(this in R3ESharedRaw raw)
    {
        ReadOnlySpan<R3ESharedRaw> single = MemoryMarshal.CreateReadOnlySpan(in raw, 1);
        return MemoryMarshal.AsBytes(single).ToArray();
    }
}

namespace RaceIntelligence.Core.Capabilities;

/// <summary>
/// Describes the set of telemetry features a simulator (via its connector) can produce.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the platform's one rule for staying simulator-agnostic, stated here and not
/// repeated elsewhere:</b> code asks <c>caps.Has(SimCapabilities.TyreWear)</c>, never
/// <c>if (sim == RaceRoom)</c>. Adding a simulator then means writing a connector that reports an
/// accurate capability set — no backend change is needed for algorithms to include or exclude its
/// data correctly.
/// </para>
/// <para>
/// Backed by <see cref="ulong"/> so up to 64 independent capabilities can be represented without
/// a breaking change to the enum's underlying storage.
/// </para>
/// </remarks>
[Flags]
public enum SimCapabilities : ulong
{
    None = 0,

    /// <summary>The connector reports per-tyre wear (0..1).</summary>
    TyreWear = 1UL << 0,

    TyrePressure = 1UL << 1,

    /// <summary>The connector reports per-tyre temperature (inner/middle/outer or similar).</summary>
    TyreTemperature = 1UL << 2,

    BrakeTemperature = 1UL << 3,

    /// <summary>The connector reports instantaneous fuel flow rate.</summary>
    FuelFlow = 1UL << 4,

    /// <summary>The connector reports an Energy Recovery System / hybrid system state.</summary>
    Ers = 1UL << 5,

    Damage = 1UL << 6,

    /// <summary>The connector reports track rubber / grip evolution.</summary>
    TrackRubber = 1UL << 7,

    /// <summary>The connector reports a push-to-pass style overtake-assist system.</summary>
    PushToPass = 1UL << 8,

    /// <summary>The connector reports a DRS (Drag Reduction System) state.</summary>
    Drs = 1UL << 9,

    /// <summary>The connector reports pit window information supplied by the simulator itself.</summary>
    PitWindow = 1UL << 10,

    /// <summary>The connector reports raw suspension telemetry beyond basic travel (e.g. velocity, ride height).</summary>
    SuspensionTelemetry = 1UL << 11,

    /// <summary>
    /// The connector reports a <see cref="RaceIntelligence.Core.Sessions.SessionStandings"/>
    /// snapshot covering every car in the session, not just the one being driven locally.
    /// </summary>
    OpponentStandings = 1UL << 12,

    /// <summary>The connector reports sector times for cars other than the local one.</summary>
    OpponentSectorTimes = 1UL << 13,

    /// <summary>The connector reports pit lane and pit stop state for cars other than the local one.</summary>
    OpponentPitState = 1UL << 14,

    /// <summary>
    /// The connector reports the driver's accumulated incident points, and the server's limit when
    /// one is set.
    /// </summary>
    /// <remarks>
    /// Deliberately has no opponent counterpart. The simulator reports this for the car it is
    /// running and for no other, so it belongs with fuel, tyre wear and damage rather than in a
    /// timing tower — a column filled in for one row out of thirty would read as "everyone else is
    /// clean", which is the opposite of the truth.
    /// </remarks>
    IncidentPoints = 1UL << 15,
}

/// <summary>
/// Extension helpers for querying <see cref="SimCapabilities"/> flag sets.
/// </summary>
public static class SimCapabilitiesExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="caps"/> contains <b>every</b> flag set
    /// in <paramref name="flag"/>, not merely one of them.
    /// </summary>
    /// <remarks>
    /// A bitwise AND against <see cref="SimCapabilities.None"/> (0) always equals 0, so
    /// <c>Has(SimCapabilities.None)</c> is always <see langword="true"/> — the correct answer for
    /// "requires nothing", which is what an algorithm touching only canonical fields declares.
    /// </remarks>
    public static bool Has(this SimCapabilities caps, SimCapabilities flag) => (caps & flag) == flag;
}

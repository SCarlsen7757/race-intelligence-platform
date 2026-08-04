namespace RaceIntelligence.Core.Capabilities;

/// <summary>
/// Describes the set of telemetry features a simulator (via its connector) can produce.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism that keeps the platform simulator-agnostic. Analysis and strategy
/// algorithms must never branch on "which simulator is this" (e.g. <c>if sim == RaceRoom</c>);
/// instead they declare which capabilities they require and callers ask
/// <c>if (session.Capabilities.Has(SimCapabilities.TyreWear))</c>. A new connector for a new
/// simulator only needs to report an accurate <see cref="SimCapabilities"/> value — no backend
/// code changes are required for the system to correctly include or exclude that simulator's
/// data from a given algorithm.
/// </para>
/// <para>
/// Backed by <see cref="ulong"/> so up to 64 independent capabilities can be represented without
/// a breaking change to the enum's underlying storage.
/// </para>
/// </remarks>
[Flags]
public enum SimCapabilities : ulong
{
    /// <summary>No optional capabilities are available.</summary>
    None = 0,

    /// <summary>The connector reports per-tyre wear (0..1).</summary>
    TyreWear = 1UL << 0,

    /// <summary>The connector reports per-tyre pressure.</summary>
    TyrePressure = 1UL << 1,

    /// <summary>The connector reports per-tyre temperature (inner/middle/outer or similar).</summary>
    TyreTemperature = 1UL << 2,

    /// <summary>The connector reports brake temperature.</summary>
    BrakeTemperature = 1UL << 3,

    /// <summary>The connector reports instantaneous fuel flow rate.</summary>
    FuelFlow = 1UL << 4,

    /// <summary>The connector reports an Energy Recovery System / hybrid system state.</summary>
    Ers = 1UL << 5,

    /// <summary>The connector reports car damage state.</summary>
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
}

/// <summary>
/// Extension helpers for querying <see cref="SimCapabilities"/> flag sets.
/// </summary>
public static class SimCapabilitiesExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="caps"/> contains every flag set in
    /// <paramref name="flag"/>.
    /// </summary>
    /// <remarks>
    /// This is the single check the rest of the platform is built around: consumers ask
    /// <c>if (caps.Has(SimCapabilities.TyreWear))</c>, never <c>if (sim == RaceRoom)</c>.
    /// Because a bitwise AND against <see cref="SimCapabilities.None"/> (0) always equals 0,
    /// <c>Has(SimCapabilities.None)</c> is always <see langword="true"/> — which is the correct
    /// answer for "requires nothing", the implicit requirement of every algorithm that only
    /// touches canonical fields.
    /// </remarks>
    /// <param name="caps">The capability set to inspect.</param>
    /// <param name="flag">The required capability or combination of capabilities.</param>
    /// <returns><see langword="true"/> if all bits in <paramref name="flag"/> are present in <paramref name="caps"/>.</returns>
    public static bool Has(this SimCapabilities caps, SimCapabilities flag) => (caps & flag) == flag;
}

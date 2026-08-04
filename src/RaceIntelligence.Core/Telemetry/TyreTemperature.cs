namespace RaceIntelligence.Core.Telemetry;

/// <summary>
/// Temperature readings across a single tyre's tread, plus the simulator's own operating window.
/// </summary>
/// <remarks>
/// <para>All temperatures are in degrees Celsius.</para>
/// <para>
/// Every field is nullable because a simulator may not report it. Connectors MUST translate their
/// "not available" sentinel (RaceRoom uses <c>-1.0</c>) to <see langword="null"/> and must never
/// pass the sentinel through as a real reading — a literal -1 °C tread temperature would be
/// silently treated as a plausible cold tyre and corrupt tyre degradation analysis. This mirrors
/// the same rule applied to throttle, brake, tyre pressure and tyre wear.
/// </para>
/// </remarks>
/// <param name="Inner">Inner tread temperature, °C. Null when not reported.</param>
/// <param name="Middle">Middle tread temperature, °C. Null when not reported.</param>
/// <param name="Outer">Outer tread temperature, °C. Null when not reported.</param>
/// <param name="Optimal">Simulator-reported optimal operating temperature, °C. Null when not reported.</param>
/// <param name="Cold">Simulator-reported threshold below which the tyre is considered cold, °C. Null when not reported.</param>
/// <param name="Hot">Simulator-reported threshold above which the tyre is considered overheating, °C. Null when not reported.</param>
public readonly record struct TyreTemperature(
    float? Inner,
    float? Middle,
    float? Outer,
    float? Optimal,
    float? Cold,
    float? Hot);

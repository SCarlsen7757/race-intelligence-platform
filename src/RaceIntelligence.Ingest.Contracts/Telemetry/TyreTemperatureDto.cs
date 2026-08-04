using MessagePack;

namespace RaceIntelligence.Ingest.Contracts.Telemetry;

/// <summary>
/// Wire form of <see cref="RaceIntelligence.Core.Telemetry.TyreTemperature"/> for a single wheel.
/// </summary>
/// <remarks>
/// <para>All temperatures are in degrees Celsius.</para>
/// <para>
/// Every field is nullable so that "the simulator did not report this" survives the wire intact.
/// Collapsing an unreported reading to a number here would make it indistinguishable from a real
/// measurement once written to the immutable telemetry table.
/// </para>
/// </remarks>
[MessagePackObject]
public sealed record TyreTemperatureDto
{
    /// <summary>Inner tread temperature, °C. Null when not reported.</summary>
    [Key(0)]
    public float? Inner { get; init; }

    /// <summary>Middle tread temperature, °C. Null when not reported.</summary>
    [Key(1)]
    public float? Middle { get; init; }

    /// <summary>Outer tread temperature, °C. Null when not reported.</summary>
    [Key(2)]
    public float? Outer { get; init; }

    /// <summary>Simulator-reported optimal operating temperature, °C. Null when not reported.</summary>
    [Key(3)]
    public float? Optimal { get; init; }

    /// <summary>Simulator-reported threshold below which the tyre is considered cold, °C. Null when not reported.</summary>
    [Key(4)]
    public float? Cold { get; init; }

    /// <summary>Simulator-reported threshold above which the tyre is considered overheating, °C. Null when not reported.</summary>
    [Key(5)]
    public float? Hot { get; init; }
}

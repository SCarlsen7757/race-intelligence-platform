using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Collector.TestSupport;

/// <summary>Builds a full set of <see cref="OperatingWindow"/> rows for tests.</summary>
public static class OperatingWindowFactory
{
    /// <summary>
    /// One window per corner, with distinct bounds so a copied or transposed index shows, and a
    /// rear-right that reports no ceiling so an absent bound can be told from a real one.
    /// </summary>
    public static IReadOnlyList<OperatingWindow> Create() =>
    [
        new(Corner.FrontLeft, Compound: 2, 90f, 60f, 110f, 410f, 200f, 600f),
        new(Corner.FrontRight, Compound: 2, 91f, 61f, 111f, 411f, 201f, 601f),
        new(Corner.RearLeft, Compound: 4, 92f, 62f, 112f, 412f, 202f, 602f),
        new(Corner.RearRight, Compound: 4, 93f, 63f, null, 413f, 203f, null),
    ];
}

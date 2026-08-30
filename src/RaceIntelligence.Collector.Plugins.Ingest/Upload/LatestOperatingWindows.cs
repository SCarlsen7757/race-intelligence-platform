using RaceIntelligence.Ingest.Contracts.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Collector.Plugins.Ingest.Upload;

/// <summary>
/// The most recent tyre and brake temperature bands the connector reported, so every telemetry
/// batch can carry them.
/// </summary>
/// <remarks>
/// <para>
/// A single reference swapped by the collect loop and read by the upload loop. No lock: the write
/// is one reference assignment and the reader wants "whatever was current when it looked", not a
/// consistent view across time. The array itself is never mutated after publication.
/// </para>
/// <para>
/// Sending the same four rows on every batch is deliberate. They are constant for a compound, the
/// server keeps the first row per <c>(session, corner, compound)</c> and drops the rest, so change
/// detection would be work done here to save a few dozen bytes and would have to be got right at
/// exactly the moment it matters least — the pit stop that switched tyres.
/// </para>
/// </remarks>
public sealed class LatestOperatingWindows
{
    private IReadOnlyList<OperatingWindow> _windows = [];

    public void Set(IReadOnlyList<OperatingWindow> windows) => _windows = windows;

    public IReadOnlyList<OperatingWindow> Current => _windows;
}

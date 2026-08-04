using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Tread temperature for a single tyre, at three points across the tread (<c>r3e_tire_temp</c>).
/// Unit: Celsius. -1.0 = N/A. Not valid for AI or remote players.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3ETireTemp
{
    /// <summary>Current temperature at three points across the tread: left, center, right (<c>R3E_TIRE_TEMP_INDEX_MAX</c>).</summary>
    public Float3 CurrentTemp;

    public float OptimalTemp;
    public float ColdTemp;
    public float HotTemp;
}

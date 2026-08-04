using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Brake temperature for a single wheel (<c>r3e_brake_temp</c>). Unit: Celsius. -1.0 = N/A. Not
/// valid for AI or remote players.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EBrakeTemp
{
    public float CurrentTemp;
    public float OptimalTemp;
    public float ColdTemp;
    public float HotTemp;
}

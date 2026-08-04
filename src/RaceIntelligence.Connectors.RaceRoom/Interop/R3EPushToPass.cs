using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>Push-to-pass overtake-assist state (<c>r3e_push_to_pass</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EPushToPass
{
    /// <summary>PTP system exists = 1, N/A = -1. For applicable systems: 2 = charging, 3 = charged.</summary>
    public int Available;

    public int Engaged;
    public int AmountLeft;
    public float EngagedTimeLeft;
    public float WaitTimeLeft;
}

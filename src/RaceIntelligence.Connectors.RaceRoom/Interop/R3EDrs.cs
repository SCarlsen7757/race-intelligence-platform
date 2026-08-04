using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>Drag Reduction System state (<c>r3e_drs</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EDrs
{
    /// <summary>If DRS is equipped and allowed. 0 = No, 1 = Yes, -1 = N/A.</summary>
    public int Equipped;

    /// <summary>Whether a DRS activation is left. 0 = No, 1 = Yes, -1 = N/A.</summary>
    public int Available;

    /// <summary>
    /// Number of DRS activations left this lap. In sessions with an 'endless' amount of
    /// activations per lap this starts at int32::max. -1 = N/A.
    /// </summary>
    public int NumActivationsLeft;

    /// <summary>DRS engaged. 0 = No, 1 = Yes, -1 = N/A.</summary>
    public int Engaged;
}

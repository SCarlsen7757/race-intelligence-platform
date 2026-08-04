using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>Current flag state (<c>r3e_flags</c>). Each field: -1 = no data, 0 = not active, 1 = active (unless noted).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EFlags
{
    /// <summary>Whether yellow flag is currently active. -1 = no data, 0 = not active, 1 = active.</summary>
    public int Yellow;

    /// <summary>Whether yellow flag was caused by current slot. -1 = no data, 0 = didn't cause it, 1 = caused it.</summary>
    public int YellowCausedIt;

    /// <summary>Whether overtaking under yellow is allowed for current slot. -1 = no data, 0 = not allowed, 1 = allowed.</summary>
    public int YellowOvertake;

    /// <summary>Positions illegally gained under yellow that must be given back. -1 = no data, 0 = none, n = count.</summary>
    public int YellowPositionsGained;

    /// <summary>Yellow flag per sector. -1 = no data, 0 = not active, 1 = active.</summary>
    public Int3 SectorYellow;

    /// <summary>Distance into track for the closest yellow. Unit: meters. -1.0 = no yellow flag exists.</summary>
    public float ClosestYellowDistanceIntoTrack;

    /// <summary>Whether blue flag is currently active.</summary>
    public int Blue;

    /// <summary>Whether black flag is currently active.</summary>
    public int Black;

    /// <summary>Whether green flag is currently active.</summary>
    public int Green;

    /// <summary>Whether checkered flag is currently active.</summary>
    public int Checkered;

    /// <summary>Whether white flag is currently active.</summary>
    public int White;

    /// <summary>
    /// Black-and-white flag state/reason. -1 = no data, 0 = not active, 1 = blue flag 1st warning,
    /// 2 = blue flag 2nd warning, 3 = wrong way, 4 = cutting track.
    /// </summary>
    public int BlackAndWhite;
}

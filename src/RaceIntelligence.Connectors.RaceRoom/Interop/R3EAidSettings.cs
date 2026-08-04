using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>Driving-aid settings (<c>r3e_aid_settings</c>). -1 = N/A unless noted.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EAidSettings
{
    /// <summary>ABS. -1 = N/A, 0 = off, 1 = on, 5 = currently active.</summary>
    public int Abs;

    /// <summary>Traction control. -1 = N/A, 0 = off, 1 = on, 5 = currently active.</summary>
    public int Tc;

    /// <summary>ESP. -1 = N/A, 0 = off, 1 = on low, 2 = on medium, 3 = on high, 5 = currently active.</summary>
    public int Esp;

    /// <summary>Countersteer assist. -1 = N/A, 0 = off, 1 = on, 5 = currently active.</summary>
    public int Countersteer;

    /// <summary>Cornering assist. -1 = N/A, 0 = off, 1 = on, 5 = currently active.</summary>
    public int Cornering;
}

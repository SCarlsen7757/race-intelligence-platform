using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Pending cut-track penalties by type (<c>r3e_cut_track_penalties</c>). -1.0 = none pending;
/// otherwise the meaning depends on the penalty type (drive-through active = 0.0, stop-and-go =
/// time to stay, slow-down = time left to give back, etc).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3ECutTrackPenalties
{
    public float DriveThrough;
    public float StopAndGo;
    public float PitStop;
    public float TimeDeduction;
    public float SlowDown;
}

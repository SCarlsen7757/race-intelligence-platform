using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>Per-car scoring/timing/state entry within the 128-entry driver array (<c>r3e_driver_data</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EDriverData
{
    public R3EDriverInfo DriverInfo;

    /// <summary>-1 = N/A, 0 = none (still on track), 1 = finished, 2 = DNF, 3 = DNQ, 4 = DNS, 5 = DQ (<c>r3e_finish_status</c>).</summary>
    public int FinishStatus;

    public int Place;

    /// <summary>Place based on class performance index.</summary>
    public int PlaceClass;

    public float LapDistance;

    /// <summary>Fraction of lap completed, 0.0-1.0. -1.0 = N/A.</summary>
    public float LapDistanceFraction;

    public R3EVec3F32 Position;
    public int TrackSector;
    public int CompletedLaps;
    public int CurrentLapValid;
    public float LapTimeCurrentSelf;
    public Float3 SectorTimeCurrentSelf;
    public Float3 SectorTimePreviousSelf;
    public Float3 SectorTimeBestSelf;
    public float TimeDeltaFront;
    public float TimeDeltaBehind;

    /// <summary>-1 = N/A, 0 = two tyres unserved, 1 = four tyres unserved, 2 = served (<c>r3e_pitstop_status</c>).</summary>
    public int PitStopStatus;

    public int InPitlane;
    public int NumPitstops;
    public R3ECutTrackPenalties Penalties;

    /// <summary>Unit: m/s.</summary>
    public float CarSpeed;

    /// <summary>Deprecated per-front/rear tyre type (<c>r3e_tire_type</c>).</summary>
    public int TireTypeFront;
    public int TireTypeRear;

    /// <summary>Deprecated per-front/rear tyre subtype (<c>r3e_tire_subtype</c>).</summary>
    public int TireSubtypeFront;
    public int TireSubtypeRear;

    public float BasePenaltyWeight;
    public float AidPenaltyWeight;

    /// <summary>-1 = unavailable, 0 = not engaged, 1 = engaged.</summary>
    public int DrsState;

    public int PtpState;

    /// <summary>-1.0 = unavailable, 0.0-1.0 tank factor.</summary>
    public float VirtualEnergy;

    /// <summary>
    /// -1 = unavailable, 0 = DriveThrough, 1 = StopAndGo, 2 = Pitstop, 3 = Time, 4 = Slowdown, 5 = Disqualify.
    /// </summary>
    public int PenaltyType;

    /// <summary>Reason code; meaning depends on <see cref="PenaltyType"/> (see r3e.h for the full table).</summary>
    public int PenaltyReason;

    /// <summary>-1 = unavailable, 0 = ignition off, 1 = ignition on not running, 2 = starter running, 3 = ignition on and running.</summary>
    public int EngineState;

    /// <summary>Orientation in Euler coordinates.</summary>
    public R3EVec3F32 Orientation;

    public float Unused1;

    public float Unused2;

    public float Unused3;
}

using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Static identity/vehicle info for a driver (<c>r3e_driver_info</c>). RaceRoom's shared memory
/// exposes only numeric ids for car/class/manufacturer here, never human-readable names — there
/// is no in-memory lookup table, so <see cref="RaceIntelligence.Core.Sessions.SessionInfo.CarName"/>
/// and friends cannot be populated from this struct alone.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EDriverInfo
{
    /// <summary>UTF-8, NUL-terminated driver name.</summary>
    public Utf8Name64 Name;

    public int CarNumber;
    public int ClassId;
    public int ModelId;
    public int TeamId;
    public int LiveryId;
    public int ManufacturerId;
    public int UserId;
    public int SlotId;
    public int ClassPerformanceIndex;

    /// <summary>0 = combustion, 1 = electric, 2 = hybrid (<c>r3e_engine_type_enum</c>).</summary>
    public int EngineType;

    public float CarWidth;
    public float CarLength;
    public float Rating;
    public float Reputation;

    /// <summary>Reserved data — DO NOT remove; required to keep every following field's offset correct.</summary>
    public float Unused1;

    /// <summary>Reserved data — DO NOT remove; required to keep every following field's offset correct.</summary>
    public float Unused2;
}

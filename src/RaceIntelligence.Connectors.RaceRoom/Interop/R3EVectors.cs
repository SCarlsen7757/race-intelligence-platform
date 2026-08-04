using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

// Small named structs from r3e.h ("r3e_vec3_f32", "r3e_vec3_f64", "r3e_ori_f32",
// "r3e_sectorStarts"). All under the header's single "#pragma pack(push, 1)" block, so every
// interop struct in this project is [StructLayout(LayoutKind.Sequential, Pack = 1)].

/// <summary>3-component single-precision vector (<c>r3e_vec3_f32</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EVec3F32
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>3-component double-precision vector (<c>r3e_vec3_f64</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EVec3F64
{
    public double X;
    public double Y;
    public double Z;
}

/// <summary>Pitch/yaw/roll orientation (<c>r3e_ori_f32</c>). Unit: radians.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EOriF32
{
    public float Pitch;
    public float Yaw;
    public float Roll;
}

/// <summary>Per-sector start distance-into-track factors (<c>r3e_sectorStarts</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3ESectorStarts
{
    public float Sector1;
    public float Sector2;
    public float Sector3;
}

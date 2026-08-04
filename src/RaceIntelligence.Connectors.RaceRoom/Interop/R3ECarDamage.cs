using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>Current damage state of the car (<c>r3e_car_damage</c>). Not valid for AI or remote players.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3ECarDamage
{
    /// <summary>Range 0.0-1.0. -1.0 = N/A.</summary>
    public float Engine;

    /// <summary>Range 0.0-1.0. -1.0 = N/A.</summary>
    public float Transmission;

    /// <summary>Range 0.0-1.0 (0.0 doesn't necessarily mean completely destroyed). -1.0 = N/A.</summary>
    public float Aerodynamics;

    /// <summary>Range 0.0-1.0. -1.0 = N/A.</summary>
    public float Suspension;

    /// <summary>Reserved data — DO NOT remove; required to keep every following field's offset correct.</summary>
    public float Unused1;

    /// <summary>Reserved data — DO NOT remove; required to keep every following field's offset correct.</summary>
    public float Unused2;
}

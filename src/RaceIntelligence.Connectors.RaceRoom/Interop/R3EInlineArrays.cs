using System.Runtime.CompilerServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

// r3e.h wraps everything in "#pragma pack(push, 1)" and uses fixed-size C arrays for every
// repeated field (tyre arrays, sector-time triples, the 128-entry driver list, UTF-8 name
// buffers, ...). C# "fixed" buffers cannot hold anything but unmanaged primitive element types,
// so a 128-element array of the (large, struct-typed) DriverData would not be expressible that
// way. [InlineArray] (C# 12+) produces a real fixed-size, stack-allocatable, blittable sequence
// of any unmanaged element type with no padding beyond the element type's own layout, which is
// exactly what's needed to mirror these C arrays byte-for-byte.

/// <summary>A fixed 64-byte UTF-8, NUL-terminated name buffer (<c>r3e_u8char[64]</c>).</summary>
[InlineArray(64)]
internal struct Utf8Name64
{
    private byte _element0;
}

/// <summary>A fixed 3-element <see cref="float"/> array (e.g. per-sector float triples).</summary>
[InlineArray(3)]
internal struct Float3
{
    private float _element0;
}

/// <summary>A fixed 4-element <see cref="float"/> array, one entry per tyre (FL, FR, RL, RR).</summary>
[InlineArray(4)]
internal struct Float4
{
    private float _element0;
}

/// <summary>A fixed 4-element <see cref="double"/> array, one entry per tyre (FL, FR, RL, RR).</summary>
[InlineArray(4)]
internal struct Double4
{
    private double _element0;
}

/// <summary>A fixed 3-element <see cref="int"/> array.</summary>
[InlineArray(3)]
internal struct Int3
{
    private int _element0;
}

/// <summary>A fixed 4-element <see cref="int"/> array, one entry per tyre (FL, FR, RL, RR).</summary>
[InlineArray(4)]
internal struct Int4
{
    private int _element0;
}

/// <summary>The 12-entry <c>pit_menu_state</c> array (<c>R3E_PIT_MENU_MAX</c>).</summary>
[InlineArray(12)]
internal struct PitMenuState12
{
    private int _element0;
}

/// <summary>Per-tyre <see cref="R3ETireTemp"/> readings, one entry per tyre (FL, FR, RL, RR).</summary>
[InlineArray(4)]
internal struct TireTemp4
{
    private R3ETireTemp _element0;
}

/// <summary>Per-tyre <see cref="R3EBrakeTemp"/> readings, one entry per tyre (FL, FR, RL, RR).</summary>
[InlineArray(4)]
internal struct BrakeTemp4
{
    private R3EBrakeTemp _element0;
}

/// <summary>
/// The <c>R3E_NUM_DRIVERS_MAX</c> (128) entry <see cref="R3EDriverData"/> array, in place order.
/// </summary>
[InlineArray(128)]
internal struct DriverDataArray128
{
    private R3EDriverData _element0;
}

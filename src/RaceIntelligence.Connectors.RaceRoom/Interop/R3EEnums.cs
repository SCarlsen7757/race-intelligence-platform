namespace RaceIntelligence.Connectors.RaceRoom.Interop;

// The raw struct stores these as plain int32 fields (matching r3e.h literally — see the comments
// on R3ESharedRaw), so these enums exist purely for readable mapping/state-machine code, not as
// the field types themselves.

/// <summary>Mirrors <c>r3e_session</c>.</summary>
internal enum R3ESessionType
{
    Unavailable = -1,
    Practice = 0,
    Qualify = 1,
    Race = 2,
    Warmup = 3,
}

/// <summary>Mirrors <c>r3e_session_phase</c>.</summary>
internal enum R3ESessionPhase
{
    Unavailable = -1,
    Garage = 1,
    Gridwalk = 2,
    Formation = 3,
    Countdown = 4,
    Green = 5,
    Checkered = 6,
}

/// <summary>Mirrors <c>r3e_control</c>.</summary>
internal enum R3EControlType
{
    Unavailable = -1,
    Player = 0,
    Ai = 1,
    Remote = 2,
    Replay = 3,
}

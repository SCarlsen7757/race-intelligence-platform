namespace RaceIntelligence.Core.Sessions;

/// <summary>
/// The canonical naming for a kind of on-track session. Note that
/// <see cref="SessionInfo.SessionType"/> holds a raw sim value cast to this type and is not
/// guaranteed to match these members — see that property before branching on one.
/// </summary>
public enum SessionType
{
    Unknown = 0,
    Practice = 1,
    Qualifying = 2,
    Race = 3,
    Warmup = 4,
}

namespace RaceIntelligence.Core.Sessions;

/// <summary>
/// The canonical naming for a kind of on-track session. Note that
/// <see cref="SessionInfo.SessionType"/> holds a raw sim value cast to this type and is not
/// guaranteed to match these members — see that property before branching on one.
/// </summary>
public enum SessionType
{
    /// <summary>The session type could not be determined.</summary>
    Unknown = 0,

    /// <summary>A free/practice session.</summary>
    Practice = 1,

    /// <summary>A qualifying session.</summary>
    Qualifying = 2,

    /// <summary>A race session.</summary>
    Race = 3,

    /// <summary>A warmup session.</summary>
    Warmup = 4,
}

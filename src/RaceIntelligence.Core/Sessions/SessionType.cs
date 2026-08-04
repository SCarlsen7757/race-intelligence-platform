namespace RaceIntelligence.Core.Sessions;

/// <summary>The kind of on-track session telemetry was collected during.</summary>
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

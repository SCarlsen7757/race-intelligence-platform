namespace RaceIntelligence.Persistence.Entities;

/// <summary>
/// Summary statistics for a single completed (or in-progress) lap. Persisted form of
/// <see cref="RaceIntelligence.Core.Sessions.LapInfo"/>.
/// </summary>
/// <remarks>
/// Keyed by <c>(session_id, lap_number)</c> rather than a surrogate id — a lap is uniquely
/// identified by its position within its session, and there is never a reason to look one up any
/// other way.
/// </remarks>
public sealed class Lap
{
    /// <summary>The session this lap belongs to. Part of the composite primary key.</summary>
    public Guid SessionId { get; set; }

    public Session? Session { get; set; }

    /// <summary>The lap number within the session. Part of the composite primary key.</summary>
    public int LapNumber { get; set; }

    /// <summary>Total lap time, if the lap was completed.</summary>
    public TimeSpan? LapTime { get; set; }

    /// <summary>Fuel consumed over the lap, in liters, if known.</summary>
    public float? FuelUsed { get; set; }

    /// <summary>Average speed over the lap, in meters per second, if known.</summary>
    public float? AvgSpeed { get; set; }

    /// <summary>Maximum speed reached during the lap, in meters per second, if known.</summary>
    public float? MaxSpeed { get; set; }

    /// <summary>
    /// A 0..1 score describing how representative this lap is for modelling purposes, computed
    /// later by the Analysis Engine. Always <see langword="null"/> when first written by the
    /// collector, which performs no analysis.
    /// </summary>
    public float? QualityScore { get; set; }

    /// <summary>Whether the simulator considered this a valid lap (e.g. not cut, not aborted).</summary>
    public bool IsValid { get; set; }
}

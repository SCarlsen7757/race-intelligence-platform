namespace RaceIntelligence.Core.Sessions;

/// <summary>
/// Summary statistics for a single completed (or in-progress) lap.
/// </summary>
public sealed record LapInfo
{
    /// <summary>The session this lap belongs to.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>The lap number within the session.</summary>
    public required int LapNumber { get; init; }

    /// <summary>Total lap time, if the lap was completed.</summary>
    public TimeSpan? LapTime { get; init; }

    /// <summary>Fuel consumed over the lap, in liters, if known.</summary>
    public float? FuelUsed { get; init; }

    /// <summary>Average speed over the lap, in meters per second, if known.</summary>
    public float? AverageSpeed { get; init; }

    /// <summary>Maximum speed reached during the lap, in meters per second, if known.</summary>
    public float? MaxSpeed { get; init; }

    /// <summary>
    /// A 0..1 score describing how representative this lap is for modelling purposes (e.g. clean
    /// vs. traffic/off-track/damage affected). Computed later by the Analysis Engine — the
    /// collector must always leave this <see langword="null"/> since it performs no analysis.
    /// </summary>
    public float? QualityScore { get; init; }

    /// <summary>Whether the simulator considered this a valid lap (e.g. not cut, not aborted).</summary>
    public bool IsValid { get; init; }
}

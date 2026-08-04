namespace RaceIntelligence.Ingest.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/sessions/{id}/laps</c>: records summary statistics for a
/// single completed (or in-progress) lap.
/// </summary>
/// <remarks>
/// Upserted by <c>(session id, LapNumber)</c> — re-submitting the same lap number (e.g. the
/// collector correcting an earlier estimate once the lap is fully known) overwrites the previous
/// values rather than creating a second row. <see cref="RaceIntelligence.Core.Sessions.LapInfo.QualityScore"/>
/// is never accepted from the wire: it is computed later by the Analysis Engine, never by the
/// collector.
/// </remarks>
/// <param name="SchemaVersion">The wire schema version this body was written against. See <see cref="SchemaVersion"/>.</param>
/// <param name="LapNumber">The lap number within the session.</param>
/// <param name="LapTime">Total lap time, if the lap was completed.</param>
/// <param name="FuelUsed">Fuel consumed over the lap, in liters, if known.</param>
/// <param name="AverageSpeed">Average speed over the lap, in meters per second, if known.</param>
/// <param name="MaxSpeed">Maximum speed reached during the lap, in meters per second, if known.</param>
/// <param name="IsValid">Whether the simulator considered this a valid lap (e.g. not cut, not aborted).</param>
public sealed record LapCompletedRequest(
    int SchemaVersion,
    int LapNumber,
    TimeSpan? LapTime,
    float? FuelUsed,
    float? AverageSpeed,
    float? MaxSpeed,
    bool IsValid);

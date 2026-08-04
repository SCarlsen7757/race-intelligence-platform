using System.Text.Json;
using CoreSessions = RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Persistence.Mapping;

/// <summary>Maps the canonical <see cref="CoreSessions.SessionInfo"/> and <see cref="CoreSessions.LapInfo"/> to their persisted row shapes.</summary>
public static class SessionMapper
{
    /// <summary>
    /// Converts a canonical session into its EF entity form. The foreign keys
    /// (<paramref name="gameVersionId"/>, <paramref name="driverId"/>,
    /// <paramref name="trackLayoutId"/>, <paramref name="carId"/>) must already have been resolved
    /// via the <c>Repositories/</c> resolve-or-create helpers — <see cref="CoreSessions.SessionInfo"/>
    /// only carries denormalized names, not ids.
    /// </summary>
    /// <param name="info">The canonical session to convert.</param>
    /// <param name="gameVersionId">The resolved <c>game_versions</c> row id for <see cref="CoreSessions.SessionInfo.GameVersion"/>.</param>
    /// <param name="driverId">The resolved <c>drivers</c> row id, if <see cref="CoreSessions.SessionInfo.PlayerName"/> was known.</param>
    /// <param name="trackLayoutId">The resolved <c>track_layouts</c> row id, if the track/layout were known.</param>
    /// <param name="carId">The resolved <c>cars</c> row id, if <see cref="CoreSessions.SessionInfo.CarName"/> was known.</param>
    /// <param name="schemaVersion">The canonical telemetry model's schema version this session's samples conform to.</param>
    /// <param name="weather">Weather conditions, if captured separately from <see cref="CoreSessions.SessionInfo.Extras"/>.</param>
    /// <param name="setup">Car setup, if captured separately from <see cref="CoreSessions.SessionInfo.Extras"/>.</param>
    public static Entities.Session ToEntity(
        CoreSessions.SessionInfo info,
        Guid gameVersionId,
        Guid? driverId,
        Guid? trackLayoutId,
        Guid? carId,
        int schemaVersion,
        JsonElement? weather = null,
        JsonElement? setup = null) => new()
        {
            Id = info.SessionId,
            GameVersionId = gameVersionId,
            DriverId = driverId,
            TrackLayoutId = trackLayoutId,
            CarId = carId,
            SimCarId = info.SimCarId,
            SimCarClassId = info.SimCarClassId,
            SimManufacturerId = info.SimManufacturerId,
            SessionType = info.SessionType,
            Capabilities = info.Capabilities,
            SchemaVersion = schemaVersion,
            Weather = weather,
            Setup = setup,
            Extras = info.Extras,
            StartedAt = info.StartedAtUtc,
            EndedAt = info.EndedAtUtc,
        };

    /// <summary>Converts a canonical lap summary into its EF entity form.</summary>
    public static Entities.Lap ToEntity(CoreSessions.LapInfo lap) => new()
    {
        SessionId = lap.SessionId,
        LapNumber = lap.LapNumber,
        LapTime = lap.LapTime,
        FuelUsed = lap.FuelUsed,
        AvgSpeed = lap.AverageSpeed,
        MaxSpeed = lap.MaxSpeed,
        QualityScore = lap.QualityScore,
        IsValid = lap.IsValid,
    };
}

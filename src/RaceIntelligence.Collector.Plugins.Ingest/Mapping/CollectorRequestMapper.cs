using System.Text.Json;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;

namespace RaceIntelligence.Collector.Plugins.Ingest.Mapping;

/// <summary>
/// Converts canonical Core <see cref="SessionInfo"/>/<see cref="LapInfo"/> into the wire request
/// DTOs the ingest API accepts.
/// </summary>
/// <remarks>
/// <c>RaceIntelligence.Ingest.Contracts.Mapping</c> ships mappers for both directions of
/// <c>TelemetrySampleDto</c> and <c>GameVersionDto</c> — reused here via
/// <see cref="GameVersionContractMapper.ToDto"/> rather than duplicated — but only the
/// DTO-to-Core direction for sessions and laps (the direction the ingest API itself needs). The
/// collector is the only component that ever needs the reverse, Core-to-DTO direction for a
/// session or lap, so that half of the mapping lives here instead of adding an unused-by-the-API
/// direction to a shared contracts project.
/// </remarks>
internal static class CollectorRequestMapper
{
    /// <summary>Builds the <c>POST /api/v1/sessions</c> request body for a newly started session.</summary>
    public static SessionCreateRequest ToSessionCreateRequest(SessionInfo session) => new(
        SchemaVersion.Current,
        session.SessionId,
        GameVersionContractMapper.ToDto(session.GameVersion),
        (ulong)session.Capabilities,
        session.TrackName,
        session.LayoutName,
        session.LayoutLengthMeters,
        (int)session.SessionType,
        session.StartedAtUtc,
        session.PlayerName,
        session.CarName,
        session.CarClassName,
        session.ManufacturerName,
        SerializeExtras(session.Extras),
        session.SimCarId,
        session.SimCarClassId,
        session.SimManufacturerId,
        session.SimDriverId,
        session.FuelUsageRate,
        session.TyreWearRate);

    /// <summary>
    /// Builds the <c>PATCH /api/v1/sessions/{id}</c> request body that records a session's end
    /// time. Every other field is left <see langword="null"/> ("leave unchanged") — the collector
    /// performs no analysis, so it has nothing else to report about a session once it ends.
    /// </summary>
    public static SessionUpdateRequest ToSessionEndedRequest(DateTimeOffset endedAtUtc) => new(
        SchemaVersion.Current,
        endedAtUtc,
        WeatherJson: null,
        SetupJson: null,
        ExtrasJson: null);

    /// <summary>Builds the <c>POST /api/v1/sessions/{id}/laps</c> request body for a completed lap.</summary>
    public static LapCompletedRequest ToLapCompletedRequest(LapInfo lap) => new(
        SchemaVersion.Current,
        lap.LapNumber,
        lap.LapTime,
        lap.FuelUsed,
        lap.AverageSpeed,
        lap.MaxSpeed,
        lap.IsValid);

    /// <summary>
    /// Serializes session <c>Extras</c> to raw JSON text, or <see langword="null"/> for an
    /// uninitialized (<see cref="JsonValueKind.Undefined"/>) element — mirrors
    /// <c>TelemetrySampleContractMapper</c>'s handling of the same situation for samples.
    /// </summary>
    private static string? SerializeExtras(JsonElement extras) =>
        extras.ValueKind == JsonValueKind.Undefined ? null : extras.GetRawText();
}

using System.Text.Json;
using CoreSessions = RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Ingest.Contracts.Mapping;

/// <summary>Converts <see cref="SessionCreateRequest"/>/<see cref="LapCompletedRequest"/> to their canonical Core forms.</summary>
public static class SessionContractMapper
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    /// <summary>
    /// Converts a session-create request into its canonical <see cref="CoreSessions.SessionInfo"/>
    /// form. <see cref="SessionCreateRequest.Capabilities"/> and
    /// <see cref="SessionCreateRequest.SessionType"/> are carried on the wire as raw numeric values
    /// (a <see cref="ulong"/> flag bit pattern and an <see langword="int"/> respectively) so the
    /// wire contract never depends on the exact enum definitions; this is where they are
    /// reinterpreted back into their typed Core forms.
    /// </summary>
    public static CoreSessions.SessionInfo ToSessionInfo(SessionCreateRequest request) => new()
    {
        SessionId = request.SessionId,
        GameVersion = GameVersionContractMapper.ToCore(request.GameVersion),
        Capabilities = (Core.Capabilities.SimCapabilities)request.Capabilities,
        TrackName = request.TrackName,
        LayoutName = request.LayoutName,
        LayoutLengthMeters = request.LayoutLengthMeters,
        SessionType = (CoreSessions.SessionType)request.SessionType,
        StartedAtUtc = request.StartedAtUtc,
        PlayerName = request.PlayerName,
        CarName = request.CarName,
        CarClassName = request.CarClassName,
        ManufacturerName = request.ManufacturerName,
        SimCarId = request.SimCarId,
        SimCarClassId = request.SimCarClassId,
        SimManufacturerId = request.SimManufacturerId,
        Extras = ParseJson(request.ExtrasJson),
    };

    /// <summary>Converts a lap-completed request into its canonical <see cref="CoreSessions.LapInfo"/> form.</summary>
    /// <param name="sessionId">The session the lap belongs to (from the route, not the body).</param>
    /// <param name="request">The lap-completed request body.</param>
    public static CoreSessions.LapInfo ToLapInfo(Guid sessionId, LapCompletedRequest request) => new()
    {
        SessionId = sessionId,
        LapNumber = request.LapNumber,
        LapTime = request.LapTime,
        FuelUsed = request.FuelUsed,
        AverageSpeed = request.AverageSpeed,
        MaxSpeed = request.MaxSpeed,
        // QualityScore is deliberately never accepted from the wire: it is computed later by the
        // Analysis Engine, never by the collector, which performs no analysis.
        QualityScore = null,
        IsValid = request.IsValid,
    };

    /// <summary>Parses an optional raw JSON string into a <see cref="JsonElement"/>, defaulting to an empty object when absent.</summary>
    private static JsonElement ParseJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? EmptyObject : JsonDocument.Parse(json).RootElement;
}

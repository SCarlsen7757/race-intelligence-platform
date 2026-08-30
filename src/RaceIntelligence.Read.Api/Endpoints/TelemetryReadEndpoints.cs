using RaceIntelligence.Persistence.Core.Repositories;
using RaceIntelligence.Read.Api.Contracts;
using RaceIntelligence.Read.Api.Mapping;

namespace RaceIntelligence.Read.Api.Endpoints;

/// <summary>
/// Reading stored telemetry, one lap at a time.
/// </summary>
/// <remarks>
/// <b>A lap is the unit, and that is a design decision rather than a first cut.</b>
/// <c>telemetry_samples</c> holds one row per sample, uncompressed, so an hour at 60 Hz is a couple
/// of hundred thousand rows for a single session — a whole-session read is a request that appears to
/// work and then takes the process with it. Every chart in the handover backlog that this endpoint
/// exists to unblock plots a lap or compares laps, and <c>ix_telemetry_session_lap</c> already
/// indexes exactly that access.
/// <para>
/// A stint- or session-wide read is a real future need — the scatters and histograms in items 19–28
/// want one — but it wants aggregation or decimation in the database, not this shape with the cap
/// raised. Leaving the cap low keeps that an explicit piece of work instead of a slow discovery.
/// </para>
/// <para>
/// Like the session endpoints, this is open and unkeyed; see <see cref="SessionReadEndpoints"/>.
/// </para>
/// </remarks>
public static class TelemetryReadEndpoints
{
    /// <summary>
    /// The most samples this endpoint will return for one lap.
    /// </summary>
    /// <remarks>
    /// A generous lap at the collector's fastest rate: roughly ten minutes at 60 Hz. Long enough
    /// that no real lap trips it, small enough that tripping it means something is wrong and should
    /// be said rather than served.
    /// </remarks>
    public const int MaxSamplesPerLap = 36_000;

    /// <summary>Maps the telemetry read endpoints. Returns the builder so a host can chain.</summary>
    public static IEndpointRouteBuilder MapTelemetryReadEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/sessions/{id:guid}/telemetry", GetLapTelemetryAsync);
        app.MapGet("/api/v1/sessions/{id:guid}/telemetry/laps", GetSampledLapsAsync);

        return app;
    }

    private static async Task<IResult> GetLapTelemetryAsync(
        Guid id,
        SessionReadRepository sessions,
        TelemetryReadRepository telemetry,
        CancellationToken ct,
        int? lap = null)
    {
        // Required, not defaulted to lap 1. Defaulting would turn "I forgot the parameter" into a
        // plausible-looking chart of the out-lap, which is the wrong answer delivered convincingly.
        if (lap is not { } lapNumber)
        {
            return ProblemResults.InvalidQuery("lap", "is required; telemetry is read one lap at a time.");
        }

        if (!await sessions.ExistsAsync(id, ct).ConfigureAwait(false))
        {
            return ProblemResults.SessionNotFound(id);
        }

        int count = await telemetry.CountForLapAsync(id, lapNumber, ct).ConfigureAwait(false);

        if (count == 0)
        {
            return ProblemResults.LapNotFound(id, lapNumber);
        }

        if (count > MaxSamplesPerLap)
        {
            return ProblemResults.LapTooLarge(count, MaxSamplesPerLap);
        }

        var samples = await telemetry.ListForLapAsync(id, lapNumber, ct).ConfigureAwait(false);

        return Results.Ok(new LapTelemetryResponse(
            id,
            lapNumber,
            [.. samples.Select(s => s.ToResponse())]));
    }

    private static async Task<IResult> GetSampledLapsAsync(
        Guid id,
        SessionReadRepository sessions,
        TelemetryReadRepository telemetry,
        CancellationToken ct)
    {
        if (!await sessions.ExistsAsync(id, ct).ConfigureAwait(false))
        {
            return ProblemResults.SessionNotFound(id);
        }

        // Which laps can actually be charted, which is not the same list as the session's laps —
        // see TelemetryReadRepository.ListSampledLapNumbersAsync for why they diverge.
        var laps = await telemetry.ListSampledLapNumbersAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(laps);
    }
}

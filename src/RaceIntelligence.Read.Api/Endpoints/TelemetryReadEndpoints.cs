using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using RaceIntelligence.Persistence.RaceRoom.Repositories;
using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Read.Api.Contracts;
using RaceIntelligence.Read.Api.Mapping;

namespace RaceIntelligence.Read.Api.Endpoints;

/// <summary>
/// Reading stored telemetry, by lap.
/// </summary>
/// <remarks>
/// <b>A lap is the unit, and that is a design decision rather than a first cut.</b>
/// <c>telemetry_samples</c> holds one row per sample, uncompressed, so an hour at 60 Hz is a couple
/// of hundred thousand rows for a single session — a whole-session read is a request that appears to
/// work and then takes the process with it. Every chart in the handover backlog that this endpoint
/// exists to unblock plots a lap or compares laps, and <c>ix_telemetry_session_lap</c> already
/// indexes exactly that access.
/// <para>
/// <b>Several laps, one request.</b> Comparing laps is the point — your best against your current,
/// or the same corner on two compounds — so the endpoint takes a list. That is not a step towards a
/// session read: the ceilings below bound it to an overlay, and the request the paragraph beneath
/// refuses is still refused however it is spelled.
/// </para>
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

    /// <summary>
    /// The most laps this endpoint will read in one request.
    /// </summary>
    /// <remarks>
    /// An overlay is two to four laps; this is deliberately roomier than that and still nowhere
    /// near a stint. It exists so "read the session by naming every lap" cannot creep in one lap at
    /// a time.
    /// </remarks>
    public const int MaxLapsPerRequest = 8;

    /// <summary>
    /// The most samples this endpoint will return across all the laps of one request.
    /// </summary>
    /// <remarks>
    /// About a dozen real laps. The per-lap cap catches a malformed lap; this one catches an
    /// overlay that is individually reasonable and collectively not.
    /// </remarks>
    public const int MaxSamplesPerRequest = 72_000;

    // The default response is the fifteen canonical fields, and stays that way. A sample is a
    // hundred and seventy-five columns; returning all of them would be about 650 bytes times several
    // thousand samples, for a chart that plots three. Anything beyond the default is asked for by
    // name — see TryResolveChannels.

    /// <summary>Maps the telemetry read endpoints. Returns the builder so a host can chain.</summary>
    public static IEndpointRouteBuilder MapTelemetryReadEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/sessions/{id:guid}/telemetry", GetLapTelemetryAsync);
        app.MapGet("/api/v1/sessions/{id:guid}/telemetry/laps", GetSampledLapsAsync);

        return app;
    }

    /// <summary>
    /// Resolves a <c>?channels=</c> list against the manifest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An allowlist, and the only one there can be.</b> The names come from
    /// <c>channels/raceroom-telemetry.channels</c>, which is also what generated the columns, so a
    /// name that resolves here is a column that exists and a name that does not is refused before it
    /// reaches any SQL.
    /// </para>
    /// <para>
    /// A group name resolves to every channel in it, because that is how a widget asks: a tyre chart
    /// wants "tyres", not a list of forty names it would have to keep in step with the manifest.
    /// </para>
    /// </remarks>
    private static bool TryResolveChannels(
        string? requested,
        out List<RaceRoomChannels.Channel> channels,
        out string? unknown)
    {
        channels = [];
        unknown = null;

        if (string.IsNullOrWhiteSpace(requested))
        {
            return true;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RaceRoomChannels.ByName.TryGetValue(name, out var channel))
            {
                if (seen.Add(channel.Name))
                {
                    channels.Add(channel);
                }

                continue;
            }

            if (RaceRoomChannels.ByGroup.TryGetValue(name, out var group))
            {
                foreach (var member in group)
                {
                    if (seen.Add(member))
                    {
                        channels.Add(RaceRoomChannels.ByName[member]);
                    }
                }

                continue;
            }

            unknown = name;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves a <c>?lap=</c> list, accepting either repeated parameters or one comma-separated
    /// value.
    /// </summary>
    /// <remarks>
    /// Both spellings because both are natural: a URL assembled in code repeats the parameter, and
    /// a URL typed by hand writes <c>lap=4,7</c>. Duplicates collapse and the result is ascending,
    /// so the response's lap order does not depend on how the caller wrote the query.
    /// </remarks>
    private static bool TryResolveLaps(string[]? requested, out List<int> laps, out string? invalid)
    {
        laps = [];
        invalid = null;

        if (requested is null)
        {
            return true;
        }

        var seen = new HashSet<int>();

        foreach (var value in requested)
        {
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lap))
                {
                    invalid = part;
                    return false;
                }

                if (seen.Add(lap))
                {
                    laps.Add(lap);
                }
            }
        }

        laps.Sort();
        return true;
    }

    private static async Task<IResult> GetLapTelemetryAsync(
        Guid id,
        SessionReadRepository sessions,
        TelemetryReadRepository telemetry,
        CancellationToken ct,
        [FromQuery(Name = "lap")] string[]? lap = null,
        string? channels = null)
    {
        if (!TryResolveLaps(lap, out var laps, out var invalid))
        {
            return ProblemResults.InvalidQuery("lap", $"names '{invalid}', which is not a lap number.");
        }

        // Required, not defaulted to lap 1. Defaulting would turn "I forgot the parameter" into a
        // plausible-looking chart of the out-lap, which is the wrong answer delivered convincingly.
        if (laps.Count == 0)
        {
            return ProblemResults.InvalidQuery("lap", "is required; telemetry is read by lap.");
        }

        if (laps.Count > MaxLapsPerRequest)
        {
            return ProblemResults.TooManyLaps(laps.Count, MaxLapsPerRequest);
        }

        // Refused by name, before any query runs. A misspelling that silently returned fewer
        // channels would draw a chart with a line missing and say nothing about why.
        if (!TryResolveChannels(channels, out var requested, out var unknown))
        {
            return ProblemResults.InvalidQuery(
                "channels",
                $"names '{unknown}', which is neither a channel nor a channel group.");
        }

        if (!await sessions.ExistsAsync(id, ct).ConfigureAwait(false))
        {
            return ProblemResults.SessionNotFound(id);
        }

        var counts = await telemetry.CountForLapsAsync(id, laps, ct).ConfigureAwait(false);

        var missing = laps.Where(number => !counts.ContainsKey(number)).ToList();
        if (missing.Count > 0)
        {
            return ProblemResults.LapsNotFound(id, missing);
        }

        foreach (var number in laps)
        {
            if (counts[number] > MaxSamplesPerLap)
            {
                return ProblemResults.LapTooLarge(number, counts[number], MaxSamplesPerLap);
            }
        }

        var total = laps.Sum(number => counts[number]);
        if (total > MaxSamplesPerRequest)
        {
            return ProblemResults.RequestTooLarge(total, MaxSamplesPerRequest);
        }

        var samples = await telemetry.ListForLapsAsync(id, laps, ct).ConfigureAwait(false);
        var extra = await telemetry
            .ListChannelsForLapsAsync(id, laps, requested, ct)
            .ConfigureAwait(false);

        // Positional alignment, as before: both queries order by (lap_number, sequence_number) over
        // the same rows. The count check is what makes a mismatch drop the channels rather than
        // pair a sample with another sample's values.
        var aligned = extra.Count == samples.Count;

        var byLap = new List<LapSamplesResponse>(laps.Count);
        var index = 0;

        // Split on the lap number changing rather than on the counts read a moment ago, so the
        // response describes the rows that actually arrived.
        while (index < samples.Count)
        {
            var number = samples[index].LapNumber;
            var rows = new List<TelemetrySampleResponse>();

            while (index < samples.Count && samples[index].LapNumber == number)
            {
                rows.Add(samples[index].ToResponse(aligned ? extra[index] : null));
                index++;
            }

            byLap.Add(new LapSamplesResponse(number, rows));
        }

        return Results.Ok(new TelemetryResponse(id, byLap));
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

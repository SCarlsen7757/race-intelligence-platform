using RaceIntelligence.Persistence.RaceRoom.Repositories;
using RaceIntelligence.Read.Api.Contracts;
using RaceIntelligence.Read.Api.Mapping;

namespace RaceIntelligence.Read.Api.Endpoints;

/// <summary>
/// Reading sessions that have already happened, and the laps they recorded.
/// </summary>
/// <remarks>
/// <b>Open, with no API key.</b> This follows the rule the live hub already states about its two
/// sockets: publishing writes what a race engineer's decisions rest on and needs a key; viewing is
/// read-only and open. A key here would have to be compiled into the browser bundle, where it is not
/// a secret and only reads as one. What guards this service is its CORS origin allowlist and the
/// fact that it cannot write — both configured by the host, since both are deployment facts.
/// <para>
/// The route prefix matches the write side's <c>/api/v1/sessions</c> deliberately. They are the same
/// resource read and written, they are simply reached at different hosts because their auth postures
/// are opposites (ADR 0003).
/// </para>
/// </remarks>
public static class SessionReadEndpoints
{
    /// <summary>Default page size when the caller does not ask for one.</summary>
    /// <remarks>The 25 that <c>docs/queries/session-overview.sql</c> settled on, which is about a screen.</remarks>
    public const int DefaultLimit = 25;

    /// <summary>Largest page this endpoint will serve.</summary>
    public const int MaxLimit = 200;

    /// <summary>Maps the session read endpoints. Returns the builder so a host can chain.</summary>
    public static IEndpointRouteBuilder MapSessionReadEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/sessions");

        group.MapGet("/", ListSessionsAsync);
        group.MapGet("/{id:guid}", GetSessionAsync);
        group.MapGet("/{id:guid}/laps", GetLapsAsync);

        return app;
    }

    private static async Task<IResult> ListSessionsAsync(
        SessionReadRepository sessions,
        CancellationToken ct,
        int? limit = null,
        DateTimeOffset? before = null)
    {
        // Clamped rather than defaulted-on-nonsense: a caller asking for 10,000 sessions has made a
        // mistake worth naming, and silently serving 200 of them would hide it behind a page that
        // looks short for no stated reason.
        if (limit is { } requested && (requested < 1 || requested > MaxLimit))
        {
            return ProblemResults.InvalidQuery("limit", $"must be between 1 and {MaxLimit}.");
        }

        int take = limit ?? DefaultLimit;

        var rows = await sessions.ListAsync(take, before, ct).ConfigureAwait(false);

        // The cursor is null on a short page, which is what tells a caller to stop. A full page is
        // reported as continuable even when it happens to be the last one — the alternative is
        // fetching one extra row on every request to find out, and one wasted request at the end of
        // a listing is cheaper than that.
        DateTimeOffset? next = rows.Count == take ? rows[^1].Session.StartedAt : null;

        return Results.Ok(new SessionPageResponse([.. rows.Select(r => r.ToResponse())], next));
    }

    private static async Task<IResult> GetSessionAsync(
        Guid id,
        SessionReadRepository sessions,
        CancellationToken ct)
    {
        var row = await sessions.FindAsync(id, ct).ConfigureAwait(false);
        return row is null ? ProblemResults.SessionNotFound(id) : Results.Ok(row.ToResponse());
    }

    private static async Task<IResult> GetLapsAsync(
        Guid id,
        SessionReadRepository sessions,
        CancellationToken ct)
    {
        // Checked separately so an unknown session is a 404 rather than an empty lap list, which
        // would be indistinguishable from a real session nobody finished a lap in.
        if (!await sessions.ExistsAsync(id, ct).ConfigureAwait(false))
        {
            return ProblemResults.SessionNotFound(id);
        }

        var laps = await sessions.ListLapsAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(laps.Select(l => l.ToResponse()).ToList());
    }
}

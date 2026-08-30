using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core.Entities;

namespace RaceIntelligence.Persistence.Core.Repositories;

/// <summary>
/// Reads stored sessions and their laps.
/// </summary>
/// <remarks>
/// <b>Read-only, and the first repository here that is.</b> Every other repository in this project
/// resolves-or-creates, because every other one is describing something a collector just observed.
/// This one answers questions about what is already there and never writes — raw telemetry is
/// immutable and permanent, and a read path is the half of that promise nobody had built.
/// <para>
/// Everything is <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/>: nothing
/// read here is ever saved, and tracking a session's worth of rows costs memory for a change set
/// that will never be inspected.
/// </para>
/// </remarks>
/// <param name="db">The simulator's telemetry store, in its schema-free shape.</param>
public sealed class SessionReadRepository(TelemetryDbContext db)
{
    /// <summary>
    /// A page of sessions, newest first, with their lap and sample counts.
    /// </summary>
    /// <remarks>
    /// Paged by a <paramref name="before"/> cursor on <c>started_at</c> rather than by offset. An
    /// offset shifts when a session is written mid-page, which silently hides a row; a cursor names
    /// where the last page stopped and cannot.
    /// <para>
    /// The counts are correlated subqueries — the <c>LEFT JOIN LATERAL</c>s in
    /// <c>docs/queries/session-overview.sql</c>, which is this query written by hand. The sample
    /// count matters to a caller in a way a lap count does not: a session with laps but no samples
    /// has nothing to chart, and the picker should be able to say so rather than offering a
    /// session that opens empty.
    /// </para>
    /// </remarks>
    /// <param name="limit">How many sessions to return. The caller is expected to have clamped this.</param>
    /// <param name="before">Return only sessions that started strictly before this, or <see langword="null"/> for the newest page.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<SessionListRow>> ListAsync(
        int limit,
        DateTimeOffset? before = null,
        CancellationToken ct = default)
    {
        var query = db.Sessions.AsNoTracking();

        if (before is { } cursor)
        {
            query = query.Where(s => s.StartedAt < cursor);
        }

        return await query
            // Id breaks ties on the timestamp. Two sessions can share a started_at — a resumed
            // session, or a fixture that writes several at once — and an unstable order there means
            // a cursor page can repeat or skip a row.
            .OrderByDescending(s => s.StartedAt)
            .ThenByDescending(s => s.Id)
            .Take(limit)
            .Select(s => new SessionListRow(
                s,
                s.Driver == null ? null : s.Driver.DisplayName,
                s.TrackLayout == null ? null : s.TrackLayout.Track!.Name,
                s.TrackLayout == null ? null : s.TrackLayout.Name,
                s.Car == null ? null : s.Car.Name,
                s.Laps.Count,
                s.TelemetrySamples.Count))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>One session with its names resolved, or <see langword="null"/> if no such session exists.</summary>
    public async Task<SessionListRow?> FindAsync(Guid id, CancellationToken ct = default) =>
        await db.Sessions
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SessionListRow(
                s,
                s.Driver == null ? null : s.Driver.DisplayName,
                s.TrackLayout == null ? null : s.TrackLayout.Track!.Name,
                s.TrackLayout == null ? null : s.TrackLayout.Name,
                s.Car == null ? null : s.Car.Name,
                s.Laps.Count,
                s.TelemetrySamples.Count))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    /// <summary>Whether a session exists. Cheaper than <see cref="FindAsync"/> when only the answer matters.</summary>
    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        await db.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);

    /// <summary>Every lap of one session, in lap order.</summary>
    /// <remarks>
    /// Unpaged. A lap count is bounded by how long a human sits in a car — hundreds at the extreme
    /// of an endurance stint — and the row is a handful of numbers. Paging it would be machinery for
    /// a size it does not reach.
    /// </remarks>
    public async Task<IReadOnlyList<Lap>> ListLapsAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.Laps
            .AsNoTracking()
            .Where(l => l.SessionId == sessionId)
            .OrderBy(l => l.LapNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}

/// <summary>
/// A session together with the names and counts that only a join can supply.
/// </summary>
/// <remarks>
/// Exists because the four names live on four other tables and the two counts are aggregates, so
/// none of them fit on <see cref="Entities.Session"/> — and materialising the navigations instead
/// would pull whole rows to read one string from each.
/// </remarks>
/// <param name="Session">The session row itself.</param>
/// <param name="DriverName">The driver's current display name, if the session has a driver.</param>
/// <param name="TrackName">The track's name, if the layout resolved.</param>
/// <param name="LayoutName">The layout's name, if the layout resolved.</param>
/// <param name="CarName">The car's name, if the car resolved.</param>
/// <param name="LapCount">How many laps this session recorded.</param>
/// <param name="SampleCount">How many telemetry samples this session recorded.</param>
public sealed record SessionListRow(
    Session Session,
    string? DriverName,
    string? TrackName,
    string? LayoutName,
    string? CarName,
    int LapCount,
    int SampleCount);

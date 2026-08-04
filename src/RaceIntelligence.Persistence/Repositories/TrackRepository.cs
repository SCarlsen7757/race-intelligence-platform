using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>Idempotent resolve-or-create access to <c>tracks</c> and <c>track_layouts</c>.</summary>
/// <remarks>
/// Follows the same insert + unique-violation-retry pattern as <see cref="GameRepository"/> — see
/// <see cref="UniqueViolationDetection"/> for why.
/// </remarks>
/// <param name="db">The context to resolve/create through.</param>
public sealed class TrackRepository(RaceIntelligenceDbContext db)
{
    /// <summary>Resolves or creates a track and one of its layouts in a single call.</summary>
    public async Task<(Track Track, TrackLayout Layout)> ResolveOrCreateAsync(
        Guid gameId,
        string trackName,
        string layoutName,
        double lengthMeters,
        string? simTrackId = null,
        string? simLayoutId = null,
        CancellationToken ct = default)
    {
        var track = await ResolveOrCreateTrackAsync(gameId, trackName, simTrackId, ct).ConfigureAwait(false);
        var layout = await ResolveOrCreateLayoutAsync(track.Id, layoutName, lengthMeters, simLayoutId, ct).ConfigureAwait(false);
        return (track, layout);
    }

    /// <summary>Resolves or creates a track by (game, name).</summary>
    public async Task<Track> ResolveOrCreateTrackAsync(Guid gameId, string trackName, string? simTrackId, CancellationToken ct = default)
    {
        var existing = await FindTrackAsync(gameId, trackName, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var track = new Track
        {
            Id = Guid.CreateVersion7(),
            GameId = gameId,
            Name = trackName,
            SimTrackId = simTrackId,
        };

        db.Tracks.Add(track);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return track;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(track).State = EntityState.Detached;
            return await FindTrackAsync(gameId, trackName, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Unique-constraint violation on tracks was reported, but the conflicting row could not be re-selected.");
        }
    }

    /// <summary>Resolves or creates a layout by (track, name).</summary>
    public async Task<TrackLayout> ResolveOrCreateLayoutAsync(
        Guid trackId,
        string layoutName,
        double lengthMeters,
        string? simLayoutId,
        CancellationToken ct = default)
    {
        var existing = await FindLayoutAsync(trackId, layoutName, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var layout = new TrackLayout
        {
            Id = Guid.CreateVersion7(),
            TrackId = trackId,
            Name = layoutName,
            LengthMeters = lengthMeters,
            SimLayoutId = simLayoutId,
        };

        db.TrackLayouts.Add(layout);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return layout;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(layout).State = EntityState.Detached;
            return await FindLayoutAsync(trackId, layoutName, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Unique-constraint violation on track_layouts was reported, but the conflicting row could not be re-selected.");
        }
    }

    private Task<Track?> FindTrackAsync(Guid gameId, string trackName, CancellationToken ct) =>
        db.Tracks.FirstOrDefaultAsync(t => t.GameId == gameId && t.Name == trackName, ct);

    private Task<TrackLayout?> FindLayoutAsync(Guid trackId, string layoutName, CancellationToken ct) =>
        db.TrackLayouts.FirstOrDefaultAsync(l => l.TrackId == trackId && l.Name == layoutName, ct);
}

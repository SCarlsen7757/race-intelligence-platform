using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>Idempotent resolve-or-create access to <c>tracks</c> and <c>track_layouts</c>. See <see cref="ResolveOrCreate"/>.</summary>
/// <param name="db">The context to resolve/create through.</param>
public sealed class TrackRepository(RaceIntelligenceDbContext db)
{
    /// <summary>Resolves or creates a track and one of its layouts in a single call.</summary>
    public async Task<(Track Track, TrackLayout Layout)> ResolveOrCreateAsync(
        Guid gameId,
        string trackName,
        string layoutName,
        double lengthMeters,
        CancellationToken ct = default)
    {
        var track = await ResolveOrCreateTrackAsync(gameId, trackName, ct).ConfigureAwait(false);
        var layout = await ResolveOrCreateLayoutAsync(track.Id, layoutName, lengthMeters, ct).ConfigureAwait(false);
        return (track, layout);
    }

    /// <summary>Resolves or creates a track by (game, name).</summary>
    public Task<Track> ResolveOrCreateTrackAsync(Guid gameId, string trackName, CancellationToken ct = default) =>
        db.RowAsync(
            token => db.Tracks.FirstOrDefaultAsync(t => t.GameId == gameId && t.Name == trackName, token),
            () => new Track
            {
                Id = Guid.CreateVersion7(),
                GameId = gameId,
                Name = trackName,
            },
            "tracks",
            ct);

    /// <summary>Resolves or creates a layout by (track, name).</summary>
    public Task<TrackLayout> ResolveOrCreateLayoutAsync(Guid trackId, string layoutName, double lengthMeters, CancellationToken ct = default) =>
        db.RowAsync(
            token => db.TrackLayouts.FirstOrDefaultAsync(l => l.TrackId == trackId && l.Name == layoutName, token),
            () => new TrackLayout
            {
                Id = Guid.CreateVersion7(),
                TrackId = trackId,
                Name = layoutName,
                LengthMeters = lengthMeters,
            },
            "track_layouts",
            ct);
}

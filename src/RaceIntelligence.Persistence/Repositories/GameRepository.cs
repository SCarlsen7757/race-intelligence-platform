using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>
/// Idempotent resolve-or-create access to <c>games</c> and <c>game_versions</c> from a Core
/// <see cref="GameVersionIdentity"/>.
/// </summary>
/// <remarks>
/// Calling <see cref="ResolveOrCreateAsync"/> twice with an identical <see cref="GameVersionIdentity"/>
/// returns the same <see cref="Game"/> and <see cref="GameVersion"/> rows both times — it never
/// creates a duplicate. Changing only <see cref="GameVersionIdentity.ConnectorVersion"/> (or any
/// other component) yields a new <see cref="GameVersion"/> row while reusing the same
/// <see cref="Game"/> row, per the unique constraint on
/// <c>(game_id, game_version, api_version_major, api_version_minor, connector_version)</c>.
/// See <see cref="UniqueViolationDetection"/> for why insert + conflict-retry was chosen over
/// <c>ON CONFLICT DO NOTHING</c>.
/// </remarks>
/// <param name="db">The context to resolve/create through.</param>
public sealed class GameRepository(RaceIntelligenceDbContext db)
{
    /// <summary>
    /// Resolves the <see cref="Game"/> and <see cref="GameVersion"/> rows for
    /// <paramref name="identity"/>, creating either or both if this is the first time they have
    /// been seen.
    /// </summary>
    public async Task<(Game Game, GameVersion Version)> ResolveOrCreateAsync(GameVersionIdentity identity, CancellationToken ct = default)
    {
        var game = await ResolveOrCreateGameAsync(identity.Game, ct).ConfigureAwait(false);
        var version = await ResolveOrCreateVersionAsync(game.Id, identity, ct).ConfigureAwait(false);
        return (game, version);
    }

    private async Task<Game> ResolveOrCreateGameAsync(GameIdentity identity, CancellationToken ct)
    {
        var existing = await db.Games.FirstOrDefaultAsync(g => g.Key == identity.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Key = identity.Key,
            Name = identity.Name,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Games.Add(game);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return game;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(game).State = EntityState.Detached;
            return await db.Games.SingleAsync(g => g.Key == identity.Key, ct).ConfigureAwait(false);
        }
    }

    private async Task<GameVersion> ResolveOrCreateVersionAsync(Guid gameId, GameVersionIdentity identity, CancellationToken ct)
    {
        var existing = await FindVersionAsync(gameId, identity, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var version = new GameVersion
        {
            Id = Guid.CreateVersion7(),
            GameId = gameId,
            GameVersionText = identity.GameVersion,
            ApiVersionMajor = identity.ApiVersionMajor,
            ApiVersionMinor = identity.ApiVersionMinor,
            ConnectorVersion = identity.ConnectorVersion,
            FirstSeenAt = DateTimeOffset.UtcNow,
        };

        db.GameVersions.Add(version);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return version;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(version).State = EntityState.Detached;
            return await FindVersionAsync(gameId, identity, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Unique-constraint violation on game_versions was reported, but the conflicting row could not be re-selected.");
        }
    }

    private Task<GameVersion?> FindVersionAsync(Guid gameId, GameVersionIdentity identity, CancellationToken ct) =>
        db.GameVersions.FirstOrDefaultAsync(
            v => v.GameId == gameId
                && v.GameVersionText == identity.GameVersion
                && v.ApiVersionMajor == identity.ApiVersionMajor
                && v.ApiVersionMinor == identity.ApiVersionMinor
                && v.ConnectorVersion == identity.ConnectorVersion,
            ct);
}

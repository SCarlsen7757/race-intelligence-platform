using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Persistence.Entities;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>
/// Idempotent resolve-or-create access to <c>game_versions</c> from a Core
/// <see cref="GameVersionIdentity"/>.
/// </summary>
/// <remarks>
/// This was <c>GameRepository</c>, and resolved a <c>games</c> row before the version that hung off
/// it. There is no <c>games</c> table any more — the database is the simulator (ADR 0001), so a
/// reference table naming which one would have exactly one row and answer a question nobody in this
/// schema can ask.
/// <para>
/// <b>The version rows stay, and that is not an inconsistency.</b> Dropping the game did not make
/// provenance less necessary: the simulator build, the telemetry API version and our own connector
/// version are what keep a row from 2026 interpretable in 2030, and a sim update that silently
/// redefines a field is exactly what they exist to catch.
/// </para>
/// <para>
/// Calling <see cref="ResolveOrCreateAsync"/> twice with an identical
/// <see cref="GameVersionIdentity"/> returns the same row both times. Changing any component —
/// <see cref="GameVersionIdentity.ConnectorVersion"/> most often — yields a new row, per the unique
/// constraint on <c>(game_version, api_version_major, api_version_minor, connector_version)</c>.
/// </para>
/// </remarks>
/// <param name="db">The context to resolve/create through.</param>
public sealed class GameVersionRepository(RaceIntelligenceDbContext db)
{
    /// <summary>
    /// Resolves the <see cref="GameVersion"/> row for <paramref name="identity"/>, creating it if
    /// this combination has not been seen before.
    /// </summary>
    /// <remarks>
    /// <see cref="GameVersionIdentity.Game"/> is deliberately not read here. Which simulator a post
    /// belongs to is settled before this point — the ingest API checks it against the simulator it
    /// is configured for and refuses a mismatch — so by the time a version is being resolved there
    /// is nothing left to scope it by.
    /// </remarks>
    public Task<GameVersion> ResolveOrCreateAsync(GameVersionIdentity identity, CancellationToken ct = default) =>
        db.RowAsync(
            token => FindAsync(identity, token),
            () => new GameVersion
            {
                Id = Guid.CreateVersion7(),
                GameVersionText = identity.GameVersion,
                ApiVersionMajor = identity.ApiVersionMajor,
                ApiVersionMinor = identity.ApiVersionMinor,
                ConnectorVersion = identity.ConnectorVersion,
                FirstSeenAt = DateTimeOffset.UtcNow,
            },
            "game_versions",
            ct);

    private Task<GameVersion?> FindAsync(GameVersionIdentity identity, CancellationToken ct) =>
        db.GameVersions.FirstOrDefaultAsync(
            v => v.GameVersionText == identity.GameVersion
                && v.ApiVersionMajor == identity.ApiVersionMajor
                && v.ApiVersionMinor == identity.ApiVersionMinor
                && v.ConnectorVersion == identity.ConnectorVersion,
            ct);
}

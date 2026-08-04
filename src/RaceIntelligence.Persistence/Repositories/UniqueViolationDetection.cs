using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RaceIntelligence.Persistence.Repositories;

/// <summary>
/// Shared helper for the resolve-or-create pattern used across this namespace: attempt an insert,
/// and if a concurrent caller won the race for the same unique key, detect that specific failure
/// (as opposed to some other, real, database error) so the caller can fall back to re-selecting
/// the row the other caller just created.
/// </summary>
/// <remarks>
/// <b>Why insert + retry-on-conflict rather than <c>ON CONFLICT DO NOTHING</c> + re-select:</b> the
/// resolve-or-create helpers operate through the EF Core change tracker (so the same
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> instance and its identity map can be
/// reused across a unit of work that also touches other entities, e.g. resolving a session's
/// driver/track/car in one save). Raw <c>ON CONFLICT</c> SQL would have to bypass the change
/// tracker and be reconciled back into it manually. The common case — no race — costs one
/// <c>SaveChanges</c>; the rare case — two collectors resolving the same new game/track/car for the
/// first time simultaneously — costs one extra round trip to re-select the row the loser lost to.
/// Both approaches give the same end state: exactly one row per unique key, ever.
/// </remarks>
internal static class UniqueViolationDetection
{
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="exception"/> was caused by a Postgres
    /// unique-constraint violation (SQLSTATE 23505), as opposed to any other failure.
    /// </summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}

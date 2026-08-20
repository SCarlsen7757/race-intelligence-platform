using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RaceIntelligence.Identity.Repositories;

/// <summary>
/// Detects a Postgres unique-constraint violation, so a claim that lost a race can be reported as a
/// conflict rather than as a server error.
/// </summary>
/// <remarks>
/// A deliberate three-line duplicate of the telemetry store's helper of the same name, and not a
/// shared one. This project must not reference <c>RaceIntelligence.Persistence.Core</c>: the registry
/// exists precisely so that identity outlives any one simulator's database, and a compile-time
/// dependency on that database's assembly would be the first crack in that. Putting it in
/// <c>Core</c> instead would drag Npgsql into the canonical model to save three lines.
/// </remarks>
public static class UniqueViolationDetection
{
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="exception"/> was caused by a Postgres
    /// unique-constraint violation (SQLSTATE 23505), as opposed to any other failure.
    /// </summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}

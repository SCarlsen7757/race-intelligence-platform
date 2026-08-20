using Microsoft.EntityFrameworkCore;

namespace RaceIntelligence.Persistence.Core.Repositories;

/// <summary>
/// The resolve-or-create body every reference-data repository in this namespace shares: look the row
/// up, insert it if it is not there, and if a concurrent caller won the race for the same unique key,
/// re-select theirs instead of failing.
/// </summary>
/// <remarks>
/// See <see cref="UniqueViolationDetection"/> for why the conflict is resolved by insert-then-retry
/// rather than <c>ON CONFLICT DO NOTHING</c>. The re-select after a conflict uses
/// <see cref="EntityFrameworkQueryableExtensions.FirstOrDefaultAsync{TSource}(IQueryable{TSource}, CancellationToken)"/>
/// plus an explicit throw, not <c>SingleAsync</c>: both raise
/// <see cref="InvalidOperationException"/> when nothing comes back, but only this one says which
/// table it was and why finding nothing there is impossible-by-construction.
/// </remarks>
internal static class ResolveOrCreate
{
    /// <summary>
    /// Returns the row <paramref name="find"/> matches, inserting the one <paramref name="create"/>
    /// builds if there is none.
    /// </summary>
    /// <param name="db">The context to resolve/create through.</param>
    /// <param name="find">Locates the row by its unique key. Called again after a lost race.</param>
    /// <param name="create">Builds the row to insert. Only called when <paramref name="find"/> found nothing.</param>
    /// <param name="table">Table name, used only in the diagnostic thrown if a reported conflict cannot be re-selected.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<TEntity> RowAsync<TEntity>(
        this TelemetryDbContext db,
        Func<CancellationToken, Task<TEntity?>> find,
        Func<TEntity> create,
        string table,
        CancellationToken ct)
        where TEntity : class
    {
        var existing = await find(ct).ConfigureAwait(false);
        return existing ?? await db.InsertRowAsync(create(), find, table, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts <paramref name="entity"/>, falling back to re-selecting the conflicting row if a
    /// concurrent caller inserted the same unique key first. For callers that have already done
    /// their own lookup (and something more than a plain return with what it found).
    /// </summary>
    /// <param name="db">The context to insert through.</param>
    /// <param name="entity">The row to insert.</param>
    /// <param name="find">Locates the row by its unique key, used only after a lost race.</param>
    /// <param name="table">Table name, used only in the diagnostic thrown if a reported conflict cannot be re-selected.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<TEntity> InsertRowAsync<TEntity>(
        this TelemetryDbContext db,
        TEntity entity,
        Func<CancellationToken, Task<TEntity?>> find,
        string table,
        CancellationToken ct)
        where TEntity : class
    {
        db.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return entity;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.Entry(entity).State = EntityState.Detached;
            return await find(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Unique-constraint violation on {table} was reported, but the conflicting row could not be re-selected.");
        }
    }
}

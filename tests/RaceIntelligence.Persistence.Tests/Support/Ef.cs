using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace RaceIntelligence.Persistence.Tests.Support;

/// <summary>
/// Disambiguates <c>SingleAsync</c> calls against a <see cref="DbSet{TEntity}"/>.
/// </summary>
/// <remarks>
/// <see cref="DbSet{TEntity}"/> implements both <see cref="IQueryable{T}"/> and
/// <see cref="IAsyncEnumerable{T}"/>, so a bare <c>dbSet.SingleAsync(predicate)</c> call is
/// ambiguous in .NET 10 between <c>Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync</c>
/// (translates the predicate to SQL) and the new BCL <c>System.Linq.AsyncEnumerable.SingleAsync</c>
/// (which would instead enumerate the entire table client-side). This helper always picks the
/// EF Core, SQL-translating overload.
/// </remarks>
internal static class Ef
{
    /// <summary>Runs <see cref="EntityFrameworkQueryableExtensions.SingleAsync{TSource}(IQueryable{TSource},Expression{Func{TSource,bool}},CancellationToken)"/> unambiguously.</summary>
    public static Task<T> SingleAsync<T>(IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        EntityFrameworkQueryableExtensions.SingleAsync(source, predicate, ct);
}

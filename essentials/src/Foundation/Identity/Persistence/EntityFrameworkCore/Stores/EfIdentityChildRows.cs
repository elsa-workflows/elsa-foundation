using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Loads the registered children of one identity aggregate with a single tracked read per child type.</summary>
/// <remarks>
/// The coordinators walk a registry of child ids and used to read one row per id. The fan-out is bounded — a
/// registry cannot exceed <c>EfIdentityStoreSupport.MaximumMaterializedListEntries</c> — but the round-trip count
/// still grew one per child, and the linked-role lookup doubled it without deduplicating links that point at the
/// same role.
///
/// Only the <em>read</em> is batched. The rows stay tracked and are removed or updated through the change tracker,
/// because every child is identity-validated before it is touched and a registered child that no longer exists is a
/// hard failure naming that child. A <c>Where(ids.Contains(x.Id)).ExecuteDeleteAsync()</c> returning 511 of 512 rows
/// could not say which child was missing, and accepting the mismatch would discard the registry-integrity check
/// these methods exist to run. So an id this load does not return is exactly the caller's existing
/// missing-row failure, raised at the same point in the same order.
/// </remarks>
internal static class EfIdentityChildRows
{
    /// <summary>
    /// Ids per query, well under every supported provider's host-parameter ceiling; SQLite's default 999 is the
    /// tightest. A registry larger than one batch issues another query rather than failing on a provider limit.
    /// </summary>
    private const int ProviderSafeIdBatchSize = 200;

    /// <summary>Loads the rows of <paramref name="set"/> whose <c>Id</c> is one of <paramref name="ids"/>.</summary>
    public static Task<Dictionary<string, TEntity>> LoadAsync<TEntity>(
        IReadOnlyCollection<string> ids,
        DbSet<TEntity> set,
        Func<TEntity, string> idOf,
        CancellationToken cancellationToken)
        where TEntity : class =>
        LoadAsync(ids, batch => set.Where(x => batch.Contains(EF.Property<string>(x, "Id"))), idOf, cancellationToken);

    public static async Task<Dictionary<string, TEntity>> LoadAsync<TEntity>(
        IReadOnlyCollection<string> ids,
        Func<string[], IQueryable<TEntity>> byIds,
        Func<TEntity, string> idOf,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var rows = new Dictionary<string, TEntity>(StringComparer.Ordinal);
        if (ids.Count == 0)
            return rows;

        foreach (var batch in ids.Distinct(StringComparer.Ordinal).Chunk(ProviderSafeIdBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var row in await byIds(batch).ToListAsync(cancellationToken))
                rows[idOf(row)] = row;
        }

        return rows;
    }
}

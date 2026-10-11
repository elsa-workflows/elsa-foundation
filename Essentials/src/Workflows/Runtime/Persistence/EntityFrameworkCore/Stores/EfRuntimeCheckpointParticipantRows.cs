using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Loads the existing rows of one checkpoint participant type with a single tracked read per type.</summary>
/// <remarks>
/// Every participant seam decides insert, update or delete from the row carrying its immutable physical identity, so
/// the id is the only thing that has to be known before the read. Reading one id per query made a commit's round-trip
/// count equal to its participant count, and no validator caps any of those collections. Loading the ids of one type
/// together leaves each decision identical while the read count becomes one per participant type per commit.
///
/// The load stays <em>by id</em> rather than by projections, which is the property the per-item reads existed to
/// protect: an id the batch does not return is treated exactly as a missing row, and a row that is returned but
/// corrupt still goes through its store's <c>ReadChecked</c> equivalent at the call site and fails closed instead of
/// becoming a false insert or a silent delete miss.
/// </remarks>
internal static class EfRuntimeCheckpointParticipantRows
{
    /// <summary>
    /// Ids per query. This is well under every supported provider's host-parameter ceiling — SQLite's default 999 is
    /// the tightest — so a commit carrying more participants than one batch holds simply issues another query rather
    /// than failing on a provider limit.
    /// </summary>
    private const int ProviderSafeIdBatchSize = 200;

    public static async ValueTask<Dictionary<string, TEntity>> LoadAsync<TEntity>(
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

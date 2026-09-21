using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// Exhaustive, bounded traversal of a Design table on its own primary key.
/// </summary>
/// <remarks>
/// Both Design EF catalogs key every row by exactly two string columns — a tenant partition key and a
/// hashed identity — and both configure an ordinal collation on every string column. Ordering on that key
/// is therefore total, index-backed and collation-safe on all four providers, which is what makes keyset
/// paging correct: no row can be visited twice or skipped, whatever concurrent writes do to other rows.
/// The key column names are read from the EF model rather than spelled here, so a model rename is a build
/// or startup failure instead of a silently partial traversal.
/// </remarks>
internal static class EfUpgradeKeysetPager
{
    public const int PageSize = 200;

    public static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        DbContext db,
        IQueryable<T> query,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(query);
        var keyProperties = db.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties;
        if (keyProperties is not { Count: 2 } || keyProperties.Any(property => property.ClrType != typeof(string)))
            throw new InvalidOperationException(
                $"{typeof(T).Name} is not keyed by exactly two string columns, so it cannot be traversed by keyset page.");
        var first = keyProperties[0].Name;
        var second = keyProperties[1].Name;

        var result = new List<T>();
        string? lastFirst = null;
        string? lastSecond = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = query;
            if (lastFirst is not null)
                page = page.Where(x =>
                    string.Compare(EF.Property<string>(x, first), lastFirst) > 0 ||
                    (EF.Property<string>(x, first) == lastFirst &&
                     string.Compare(EF.Property<string>(x, second), lastSecond) > 0));
            // The key columns are shadow properties on some of these entities, so they are projected with
            // the row rather than read back from the materialized instance, which does not carry them.
            var rows = await page
                .OrderBy(x => EF.Property<string>(x, first))
                .ThenBy(x => EF.Property<string>(x, second))
                .Take(PageSize)
                .Select(x => new KeyedRow<T>(x, EF.Property<string>(x, first), EF.Property<string>(x, second)))
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return result;
            result.AddRange(rows.Select(x => x.Row));
            lastFirst = rows[^1].First;
            lastSecond = rows[^1].Second;
            if (rows.Count < PageSize)
                return result;
        }
    }

    private sealed record KeyedRow<T>(T Row, string First, string Second);
}

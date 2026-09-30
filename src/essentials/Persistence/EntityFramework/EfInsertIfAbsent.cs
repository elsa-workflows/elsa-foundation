using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Creates one row that several hosts create at once when they start together, and lets the ones that lose the race carry
/// on: the shared "insert unless it is there" step of the startup writes that nothing else orders, the database identity
/// row and a family's first finalization record (#2162). The caller reads the row afterwards, whoever inserted it.
/// </summary>
/// <remarks>
/// <para>
/// <c>SaveChanges</c> cannot do this quietly. A losing insert raises the engine's duplicate-key error, and EF logs that
/// as two errors of its own (the failed command and the failed save) before any handler here sees the exception, so a
/// race that is benign by design would still put <c>fail:</c> lines in the loser's log. The statement is therefore one that
/// cannot fail on a duplicate: each engine's own insert-unless-present form, which the database decides atomically.
/// </para>
/// <para>
/// This is the one place a store writes engine-specific SQL, three small shapes over the four engines, because no
/// portable statement is both atomic and error-free (a <c>WHERE NOT EXISTS</c> guard alone still loses a race at the
/// key). It goes through EF's command pipeline, so command interceptors see it, but not through <c>SaveChanges</c>, so
/// the context's save interceptors do not: it is for rows no write gate stamps. It runs in the context's current
/// transaction when it has one, and it tracks nothing and saves nothing else the context holds.
/// </para>
/// </remarks>
internal static class EfInsertIfAbsent
{
    /// <summary>Inserts <paramref name="row"/> into its entity's table unless a row with its primary key is already there.</summary>
    /// <exception cref="NotSupportedException">The context's engine is not one of the four first-party engines.</exception>
    public static async Task InsertAsync(DbContext context, object row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(row);
        var entityType = context.Model.FindEntityType(row.GetType())
                         ?? throw new InvalidOperationException($"{context.GetType().Name} does not map {row.GetType().Name}.");
        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
                    ?? throw new InvalidOperationException($"{entityType.DisplayName()} is not mapped to a table.");
        var columns = entityType.GetProperties().Where(property => !property.IsShadowProperty()).ToArray();
        var keys = entityType.FindPrimaryKey()!.Properties;
        var sql = context.GetService<ISqlGenerationHelper>();

        string Column(IProperty property) => sql.DelimitIdentifier(property.GetColumnName(table)!);
        var name = sql.DelimitIdentifier(table.Name, table.Schema);
        var insert = $"INSERT INTO {name} ({string.Join(", ", columns.Select(Column))}) VALUES ({string.Join(", ", columns.Select((_, index) => $"{{{index}}}"))})";
        var command = context.Database.ProviderName switch
        {
            EfProviderNames.Sqlite or EfProviderNames.PostgreSql => $"{insert} ON CONFLICT DO NOTHING",
            EfProviderNames.MySql => $"{insert} ON DUPLICATE KEY UPDATE {Column(keys[0])} = {Column(keys[0])}",
            // HOLDLOCK makes the existence check and the insert one serializable step, so of two racing hosts the second waits, then finds the row.
            EfProviderNames.SqlServer =>
                $"IF NOT EXISTS (SELECT 1 FROM {name} WITH (UPDLOCK, HOLDLOCK) WHERE {string.Join(" AND ", keys.Select(key => $"{Column(key)} = {{{Array.IndexOf(columns, key)}}}"))}) {insert}",
            var other => throw new NotSupportedException($"Creating a startup row is not supported on '{other}'; the first-party engines are {EfProviderNames.Sqlite}, {EfProviderNames.SqlServer}, {EfProviderNames.PostgreSql} and {EfProviderNames.MySql}.")
        };

        await context.Database.ExecuteSqlRawAsync(command, columns.Select(column => ProviderValue(column, row)).ToArray(), cancellationToken);
    }

    private static object? ProviderValue(IProperty column, object row)
    {
        var value = column.GetGetter().GetClrValue(row);
        return column.GetRelationalTypeMapping().Converter is { } converter && value is not null ? converter.ConvertToProvider(value) : value;
    }
}

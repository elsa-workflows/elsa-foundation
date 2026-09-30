using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// Creates one row that several hosts create at once when they start together, and lets the ones that lose the race carry
/// on: the "insert unless it is there" step of the two startup writes that nothing else orders, the database identity row
/// and a family's first finalization record (#2162). The caller reads the row afterwards, whoever inserted it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>SaveChanges</c>.</b> A losing insert raises the engine's duplicate-key error, and EF logs that as two
/// errors of its own (the failed command and the failed save) before any handler sees the exception, so a race that is
/// benign by design would still put <c>fail:</c> lines in the loser's log. The statement is therefore one that cannot
/// fail on a duplicate: each engine's own insert-unless-present form, which the database decides atomically. This is the
/// one place in <c>src/</c> that writes engine-specific SQL, permitted for race-prone writes by ADR 0073, D3, and guarded
/// to this file by <c>RawSqlArchitectureTests</c>. No portable statement is both atomic and error-free: a
/// <c>WHERE NOT EXISTS</c> guard alone loses the race at the key, and on SQL Server it must be one statement with the
/// insert, because a guard that is a statement of its own releases its lock before the insert runs.
/// </para>
/// <para>
/// <b>Precondition: no transaction of the caller's.</b> The statement runs in autocommit, and the guarantees above hold
/// only there. Inside a transaction the engines differ: PostgreSQL at <c>REPEATABLE READ</c> fails the loser with "could
/// not serialize access", MySQL at <c>REPEATABLE READ</c> keeps the winner's row out of the loser's read-back after an
/// earlier read, and on MySQL a winner that rolls back deadlocks the waiters. No first-party caller has one, so a context
/// with a <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.CurrentTransaction"/> is refused.
/// </para>
/// <para>
/// It goes through EF's command pipeline, so command interceptors and logging see it, but not through
/// <c>SaveChanges</c>, so the context's save interceptors do not: it is for rows no write gate stamps. It tracks
/// nothing and saves nothing else the context holds. Each value travels in a parameter named for its column.
/// </para>
/// </remarks>
internal static class EfInsertIfAbsent
{
    /// <summary>Inserts <paramref name="row"/> into its entity's table unless a row with its primary key is already there.</summary>
    /// <exception cref="InvalidOperationException">The context has a transaction open, or does not map the row.</exception>
    /// <exception cref="NotSupportedException">The context's engine is not one of the four first-party engines.</exception>
    public static async Task InsertAsync(DbContext context, object row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(row);
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A startup row is created in autocommit; the context already has a transaction open.");

        var entityType = context.Model.FindEntityType(row.GetType())
                         ?? throw new InvalidOperationException($"{context.GetType().Name} does not map {row.GetType().Name}.");
        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
                    ?? throw new InvalidOperationException($"{entityType.DisplayName()} is not mapped to a table.");
        var columns = entityType.GetProperties().Where(property => !property.IsShadowProperty()).ToArray();
        var keys = entityType.FindPrimaryKey()!.Properties;
        var sql = context.GetService<ISqlGenerationHelper>();

        string Column(IProperty property) => sql.DelimitIdentifier(property.GetColumnName(table)!);
        string Value(IProperty property) => $"{{{Array.IndexOf(columns, property)}}}";
        var name = sql.DelimitIdentifier(table.Name, table.Schema);
        var insert = $"INSERT INTO {name} ({string.Join(", ", columns.Select(Column))})";
        var values = string.Join(", ", columns.Select(Value));
        var command = context.Database.ProviderName switch
        {
            EfProviderNames.Sqlite or EfProviderNames.PostgreSql => $"{insert} VALUES ({values}) ON CONFLICT DO NOTHING",
            EfProviderNames.MySql => $"{insert} VALUES ({values}) ON DUPLICATE KEY UPDATE {Column(keys[0])} = {Column(keys[0])}",
            EfProviderNames.SqlServer =>
                $"{insert} SELECT {values} WHERE NOT EXISTS (SELECT 1 FROM {name} WITH (UPDLOCK, HOLDLOCK) WHERE {string.Join(" AND ", keys.Select(key => $"{Column(key)} = {Value(key)}"))})",
            var other => throw new NotSupportedException($"Creating a startup row is not supported on '{other}'; the first-party engines are {EfProviderNames.Sqlite}, {EfProviderNames.SqlServer}, {EfProviderNames.PostgreSql} and {EfProviderNames.MySql}.")
        };

        await using var carrier = context.Database.GetDbConnection().CreateCommand();
        var parameters = columns.Select(column => (object)column.GetRelationalTypeMapping()
            .CreateParameter(carrier, column.GetColumnName(table)!, column.GetGetter().GetClrValue(row))).ToArray();
        await context.Database.ExecuteSqlRawAsync(command, parameters, cancellationToken);
    }
}

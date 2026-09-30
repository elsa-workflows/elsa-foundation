using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Inserts one row that several hosts create at once when they start together, and lets the one that loses the race
/// carry on: the shared "insert, or read the winner's row" step of the startup writes that nothing else orders, the
/// database identity row and the finalization record seed (#2162).
/// </summary>
/// <remarks>
/// <para>
/// <c>SaveChanges</c> cannot do this quietly. A losing insert raises the engine's duplicate-key error, and EF logs that
/// as two errors of its own (the failed command and the failed save) before any handler here sees the exception, so a
/// race that is benign by design would still put <c>fail:</c> lines in the loser's log. The insert is therefore sent as
/// one plain <c>INSERT</c> on the context's own connection, which EF does not log, and a unique-key violation is read
/// as "another creator inserted first" and reported as such. The statement is standard SQL on every supported engine,
/// so there is no provider branching: the identifiers are delimited by the provider's own generation helper, and each
/// value goes through the column's relational type mapping, converter included.
/// </para>
/// <para>
/// The row is never tracked, and nothing else the context holds is saved. Like <c>ExecuteUpdate</c>, it does not pass
/// the context's <c>SaveChanges</c> interceptors, so it is for rows no write gate stamps. It runs in the context's
/// current transaction when it has one; on an engine that aborts a transaction at a failed statement, a caller that
/// races inside one must expect that, as it would from <c>SaveChanges</c>.
/// </para>
/// </remarks>
internal static class EfInsertIfAbsent
{
    /// <summary>
    /// Inserts <paramref name="row"/> into its entity's table, and returns whether this call inserted it. False means a
    /// row with the same key, or another unique key, exists already, and the caller reads it.
    /// </summary>
    public static async Task<bool> InsertAsync(DbContext context, object row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(row);
        var entityType = context.Model.FindEntityType(row.GetType())
                         ?? throw new InvalidOperationException($"{context.GetType().Name} does not map {row.GetType().Name}.");
        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
                    ?? throw new InvalidOperationException($"{entityType.DisplayName()} is not mapped to a table.");
        var columns = entityType.GetProperties().Where(property => !property.IsShadowProperty()).ToArray();
        var sql = context.GetService<ISqlGenerationHelper>();

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            var names = columns.Select((_, index) => sql.GenerateParameterName($"p{index}")).ToArray();
            command.CommandText =
                $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} " +
                $"({string.Join(", ", columns.Select(column => sql.DelimitIdentifier(column.GetColumnName(table)!)))}) " +
                $"VALUES ({string.Join(", ", names)})";
            for (var index = 0; index < columns.Length; index++)
                command.Parameters.Add(columns[index].GetRelationalTypeMapping().CreateParameter(
                    command, names[index], columns[index].GetGetter().GetClrValue(row)));

            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (DbException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            return false;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}

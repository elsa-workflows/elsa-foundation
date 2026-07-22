using System.Data;
using System.Data.Common;
using System.Globalization;
using Elsa.Persistence.EFCore.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EFCore.Services;

/// <inheritdoc />
public sealed class MigrationsLockReclaimer(ILogger<MigrationsLockReclaimer> logger) : IMigrationsLockReclaimer
{
    // Only the SQLite provider uses a persistent table-based migrations lock that survives process death.
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    // The lock table name is hard-coded by EF Core's SqliteHistoryRepository (independent of the
    // configurable history/schema table) and is not exposed as public API, so it is mirrored here.
    private const string LockTableName = "__EFMigrationsLock";

    /// <inheritdoc />
    public async Task<int> ReclaimStaleLocksAsync(DbContext dbContext, TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        // SqlServer (sp_getapplock) and PostgreSQL (advisory locks) release their migration lock when the
        // connection drops, so an abandoned lock cannot wedge them. Only SQLite needs reclamation.
        if (!string.Equals(dbContext.Database.ProviderName, SqliteProviderName, StringComparison.Ordinal))
            return 0;

        if (staleAfter <= TimeSpan.Zero)
            return 0;

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            // Nothing to reclaim on a fresh database that has never held the lock.
            if (!await LockTableExistsAsync(connection, cancellationToken))
                return 0;

            var lockRow = await ReadLockRowAsync(connection, cancellationToken);

            if (lockRow is null)
                return 0;

            var (id, timestampText) = lockRow.Value;

            // Only reclaim a lock demonstrably older than the staleness window; a lock a concurrent migrator
            // just acquired (same SQLite file, different DbContext) carries a recent timestamp and is left
            // untouched. An unparseable timestamp can only have come from an earlier, now-dead writer, so it
            // is treated as stale.
            if (DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)
                && DateTimeOffset.UtcNow - timestamp < staleAfter)
                return 0;

            // Compare-and-delete on the exact timestamp text read above: if the lock was re-acquired in the
            // meantime (delete + re-insert with a new timestamp) this matches nothing and leaves it in place.
            var deleted = await DeleteLockRowAsync(connection, id, timestampText, cancellationToken);

            if (deleted > 0)
                logger.LogWarning(
                    "Reclaimed a stale EF Core migrations lock (table '{LockTable}', timestamp '{Timestamp}') that a previous process left behind without releasing. The lock had not been refreshed for at least {StaleAfter}; the usual cause is a crash or forced shutdown during an earlier migration.",
                    LockTableName, timestampText, staleAfter);

            return deleted;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> LockTableExistsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"sqlite_master\" WHERE \"type\" = 'table' AND \"name\" = $name;";
        AddParameter(command, "$name", LockTableName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<(long Id, string TimestampText)?> ReadLockRowAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Id\", \"Timestamp\" FROM \"{LockTableName}\" LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var id = reader.GetInt64(0);
        var timestampText = reader.GetString(1);
        return (id, timestampText);
    }

    private static async Task<int> DeleteLockRowAsync(DbConnection connection, long id, string timestampText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM \"{LockTableName}\" WHERE \"Id\" = $id AND \"Timestamp\" = $timestamp;";
        AddParameter(command, "$id", id);
        AddParameter(command, "$timestamp", timestampText);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

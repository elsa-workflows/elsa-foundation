using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Persistence.EFCore.Sqlite.Interceptors;

/// <summary>
/// Enables write-ahead logging on file-based SQLite databases so readers (e.g. diagnostics UI queries) do
/// not block the background drain writers with SQLITE_LOCKED/SQLITE_BUSY (issue #607). WAL is a persistent
/// database property, but re-issuing the pragma on an already-WAL database is a cheap no-op, so it simply
/// runs on every connection open. In-memory databases do not support WAL and are skipped.
/// </summary>
public sealed class SqliteWalConnectionInterceptor : DbConnectionInterceptor
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly SqliteWalConnectionInterceptor Instance = new();

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        EnableWriteAheadLogging(connection);

    /// <inheritdoc />
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        // The pragma is a sub-millisecond local statement; executing it synchronously avoids an async
        // state machine on every pooled connection open.
        EnableWriteAheadLogging(connection);
        return Task.CompletedTask;
    }

    private static void EnableWriteAheadLogging(DbConnection connection)
    {
        if (IsInMemory(connection.ConnectionString))
            return;

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
    }

    private static bool IsInMemory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        return builder.Mode == SqliteOpenMode.Memory ||
               string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
               builder.DataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase);
    }
}

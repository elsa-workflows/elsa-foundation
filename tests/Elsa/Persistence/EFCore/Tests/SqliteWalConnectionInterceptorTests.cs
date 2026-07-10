using Elsa.Persistence.EFCore.Sqlite.Constants;
using Elsa.Persistence.EFCore.Sqlite.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Covers issue #607: the default SQLite connection string must be private-cache (shared-cache readers
/// take table locks that fail a concurrent writer with SQLITE_LOCKED), and file-based databases get
/// concurrent readers via write-ahead logging applied on connection open instead. In-memory databases
/// keep their journal mode untouched.
/// </summary>
public sealed class SqliteWalConnectionInterceptorTests
{
    [Fact]
    public void DefaultConnectionStringIsPrivateCache() =>
        Assert.DoesNotContain("Cache=Shared", SqliteConstants.DefaultConnectionString, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void FileBasedDatabaseIsSwitchedToWriteAheadLogging()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-wal-test-{Guid.NewGuid():N}.db");
        try
        {
            Assert.Equal("wal", QueryJournalMode($"Data Source={path}"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, $"{path}-wal", $"{path}-shm" })
                File.Delete(file);
        }
    }

    [Fact]
    public void InMemoryDatabaseKeepsItsJournalMode()
    {
        // Keep a root connection open so the shared in-memory database outlives the pooled context connection.
        using var root = new SqliteConnection($"Data Source=wal-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        root.Open();

        Assert.Equal("memory", QueryJournalMode(root.ConnectionString));
    }

    private static string QueryJournalMode(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<JournalModeDbContext>();
        builder.UseElsaSqlite(typeof(SqliteWalConnectionInterceptorTests).Assembly, connectionString);
        using var db = new JournalModeDbContext(builder.Options);

        // Open through EF so the connection interceptor fires, exactly like production traffic.
        db.Database.OpenConnection();
        var connection = db.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return (string)command.ExecuteScalar()!;
    }

    private sealed class JournalModeDbContext(DbContextOptions<JournalModeDbContext> options) : DbContext(options);
}

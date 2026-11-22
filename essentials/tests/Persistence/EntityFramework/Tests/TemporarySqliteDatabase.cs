using Microsoft.Data.Sqlite;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// A private, temp-directory SQLite database for a test fixture: owns the file path, the connection string, and a
/// teardown that clears that connection string's pool before removing the database and its
/// <c>-wal</c>/<c>-shm</c>/<c>-journal</c> sidecars.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft.Data.Sqlite pools connections by default. A bare <c>File.Delete</c> after the last <c>DbContext</c> is
/// disposed can still find the file handle held by the pool, which is harmless on macOS/Linux (an open file can be
/// unlinked) but throws <see cref="IOException"/> on Windows, or, when a handle is held by something outside the
/// pool entirely (an antivirus scanner, a lingering lock), <see cref="UnauthorizedAccessException"/>. Clearing the
/// pool first avoids that in the common case; the delete loop still swallows a residual lock of either kind so
/// teardown never fails an otherwise-green test. Each EF
/// module's test project compiles this file in, the same way it compiles in <see cref="UnorderedRowLimitGuard"/>.
/// A few older fixtures track their own generated path across many call sites instead of holding an instance of
/// this type; <see cref="ClearPoolAndDeleteFiles"/> lets their teardown delegate to the same logic.
/// </para>
/// <para>
/// The teardown clears only the pool of the connection string the test opened its database with, never
/// <c>SqliteConnection.ClearAllPools</c> (#2185). Clearing a pool also reclaims any connection of it that looks
/// leaked, and Microsoft.Data.Sqlite 10.0.10 marks a connection it is lending out as active before it records the
/// owner (dotnet/efcore#39008; upstream reorders this in 10.0.13, which #2220 tracks). A process-wide clear from one
/// test's teardown can therefore reclaim and dispose the native handle another test is opening, and that test fails at
/// <c>Open</c> with an <see cref="ObjectDisposedException"/> for <c>SQLitePCL.sqlite3</c>. No other test opens a test's
/// own connection string, so clearing that pool at teardown cannot meet another test's open. A pool is keyed by the
/// exact connection string, so a test that opens this database with other options takes its connection string from
/// <see cref="ConnectionStringWith"/>, and the teardown clears that pool too. The architecture guard
/// <c>SqlitePoolClearingGuardTests</c> keeps process-wide clears out of the backend modules, hosts and tests.
/// </para>
/// </remarks>
internal sealed class TemporarySqliteDatabase : IAsyncDisposable
{
    private readonly List<string> _connectionStrings = [];

    public TemporarySqliteDatabase(string? name = null)
        => Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), $"elsa-{name ?? "test"}-{Guid.NewGuid():N}.db");

    public string Path { get; }

    public string ConnectionString => $"Data Source={Path}";

    /// <summary>A connection string to this database with other <paramref name="options"/>, whose pool the teardown also clears.</summary>
    public string ConnectionStringWith(string options)
    {
        var connectionString = $"{ConnectionString};{options}";
        _connectionStrings.Add(connectionString);
        return connectionString;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            SqliteConnection.ClearPool(new SqliteConnection(connectionString));
        ClearPoolAndDeleteFiles(ConnectionString);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears the pool of <paramref name="connectionString"/>, and no other, then deletes the database file it names and
    /// that file's sidecars.
    /// </summary>
    public static void ClearPoolAndDeleteFiles(string connectionString)
    {
        SqliteConnection.ClearPool(new SqliteConnection(connectionString));
        var path = new SqliteConnectionStringBuilder(connectionString).DataSource;
        foreach (var file in new[] { path, $"{path}-wal", $"{path}-shm", $"{path}-journal" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A residual lock from the pool, a slow filesystem, or a handle held by something outside the pool
                // (e.g. an antivirus scanner on Windows) should never fail an otherwise-green test.
            }
        }
    }
}

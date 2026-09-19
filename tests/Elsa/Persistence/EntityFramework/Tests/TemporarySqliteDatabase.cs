using Microsoft.Data.Sqlite;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// A private, temp-directory SQLite database for a test fixture: owns the file path, the connection string, and a
/// teardown that clears the connection pool before removing the database and its <c>-wal</c>/<c>-shm</c> sidecars.
/// </summary>
/// <remarks>
/// Microsoft.Data.Sqlite pools connections by default. A bare <c>File.Delete</c> after the last <c>DbContext</c> is
/// disposed can still find the file handle held by the pool, which is harmless on macOS/Linux (an open file can be
/// unlinked) but throws <see cref="IOException"/> on Windows. Clearing the pool first avoids that in the common
/// case; the delete loop still swallows a residual lock so teardown never fails an otherwise-green test. Each EF
/// module's test project compiles this file in, the same way it compiles in <see cref="UnorderedRowLimitGuard"/>.
/// A few older fixtures track their own generated path across many call sites instead of holding an instance of
/// this type; <see cref="ClearPoolAndDeleteFiles"/> lets their teardown delegate to the same logic.
/// </remarks>
internal sealed class TemporarySqliteDatabase : IAsyncDisposable
{
    public TemporarySqliteDatabase(string? name = null)
        => Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), $"elsa-{name ?? "test"}-{Guid.NewGuid():N}.db");

    public string Path { get; }

    public string ConnectionString => $"Data Source={Path}";

    public ValueTask DisposeAsync()
    {
        ClearPoolAndDeleteFiles(Path);
        return ValueTask.CompletedTask;
    }

    public static void ClearPoolAndDeleteFiles(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A residual lock from the pool, or a slow filesystem, should never fail an otherwise-green test.
            }
        }
    }
}

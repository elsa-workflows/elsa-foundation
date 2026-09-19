using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Windows is the only platform where a pooled connection actually holds the file handle open at teardown time
/// (see <see cref="TemporarySqliteDatabase"/>'s remarks), so this cannot reproduce the reported IOException by
/// locking the file: unlinking an open file succeeds on macOS/Linux. What is checkable here is that the teardown
/// removes the database and its sidecars, and that it tolerates a delete failure rather than throwing - exercised
/// below with a path long enough that the filesystem itself refuses the delete.
/// </summary>
public sealed class TemporarySqliteDatabaseTests
{
    [Fact]
    public async Task DisposeAsync_removes_the_database_and_all_sidecars()
    {
        var database = new TemporarySqliteDatabase("helper-test");
        await File.WriteAllTextAsync(database.Path, "database");
        await File.WriteAllTextAsync($"{database.Path}-wal", "wal");
        await File.WriteAllTextAsync($"{database.Path}-shm", "shm");
        await File.WriteAllTextAsync($"{database.Path}-journal", "journal");

        await database.DisposeAsync();

        Assert.False(File.Exists(database.Path));
        Assert.False(File.Exists($"{database.Path}-wal"));
        Assert.False(File.Exists($"{database.Path}-shm"));
        Assert.False(File.Exists($"{database.Path}-journal"));
    }

    [Fact]
    public async Task DisposeAsync_is_a_no_op_when_nothing_was_ever_written()
    {
        var database = new TemporarySqliteDatabase("helper-test-unused");

        await database.DisposeAsync();
    }

    [Fact]
    public void ClearPoolAndDeleteFiles_does_not_throw_when_a_file_cannot_be_deleted()
    {
        // A path this long makes File.Delete throw PathTooLongException - an IOException - on every platform,
        // standing in for the residual pool lock this helper exists to tolerate.
        var path = Path.Join(Path.GetTempPath(), new string('a', 5000) + ".db");

        TemporarySqliteDatabase.ClearPoolAndDeleteFiles(path);
    }

    [Fact]
    public void ClearPoolAndDeleteFiles_does_not_throw_on_an_UnauthorizedAccessException()
    {
        // File.Delete on a directory throws UnauthorizedAccessException on every platform (not just Windows), so
        // this stands in for the Windows-only case - an antivirus scanner or a lingering lock - the widened catch
        // exists to tolerate.
        var path = Path.Join(Path.GetTempPath(), $"elsa-helper-test-dir-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(path);
        try
        {
            TemporarySqliteDatabase.ClearPoolAndDeleteFiles(path);
        }
        finally
        {
            Directory.Delete(path);
        }
    }
}

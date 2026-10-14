using System.Text.RegularExpressions;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// No backend module, host or test clears every Microsoft.Data.Sqlite pool in the process (#2185).
/// <para>
/// Clearing a pool reclaims and disposes any of its connections that look leaked, and Microsoft.Data.Sqlite 10.0.10
/// marks a connection it is lending out as active before it records the owner (dotnet/efcore#39008). For that moment the
/// connection looks leaked. <c>SqliteConnection.ClearAllPools</c> clears every pool, so a call made while anything else
/// in the process opens a pooled connection can dispose that connection's native handle, and the open fails with an
/// <see cref="ObjectDisposedException"/> for <c>SQLitePCL.sqlite3</c>. Test teardowns did exactly that while other test
/// classes in the same assembly ran in parallel. A teardown clears the pools of its own connection strings instead
/// (<c>SqliteConnection.ClearPool</c>, or <c>TemporarySqliteDatabase</c>, which does it).
/// </para>
/// </summary>
public sealed class SqlitePoolClearingGuardTests
{
    private static readonly Regex ProcessWideClear = new(@"\bSqliteConnection\s*\.\s*ClearAllPools\s*\(", RegexOptions.Compiled);

    [Fact]
    public void No_source_clears_every_sqlite_pool_in_the_process()
    {
        var scanned = 0;
        var violations = new List<string>();

        foreach (var file in ModuleRoots.Resolve(RepoRoot, ModuleRoots.All)
                     .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                     .Where(path => !IsBuildOutput(path)))
        {
            scanned++;
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (ClearsEveryPool(lines[index]))
                    violations.Add($"{Path.GetRelativePath(RepoRoot, file)}:{index + 1}");
            }
        }

        Assert.True(scanned > 0, "The SQLite pool-clearing guard scanned no source files; its roots are wrong.");
        Assert.True(
            violations.Count == 0,
            $"{violations.Count} call(s) clear every SQLite pool in the process; clear the pools of your own connection strings instead (#2185):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Mutation proof: the predicate the guard uses flags a process-wide clear in either spelling, and nothing narrower. The
    /// flagged samples are split so that this file does not trip the sweep above.
    /// </summary>
    [Theory]
    [InlineData("        SqliteConnection" + ".ClearAllPools();", true)]
    [InlineData("        Microsoft.Data.Sqlite.SqliteConnection" + ".ClearAllPools();", true)]
    [InlineData("SqliteConnection" + " . ClearAllPools ( );", true)]
    [InlineData("        SqliteConnection.ClearPool(new SqliteConnection(connectionString));", false)]
    [InlineData("        NpgsqlConnection.ClearAllPools();", false)]
    [InlineData("/// clearing every pool (<c>SqliteConnection.ClearAllPools</c>) can dispose another test's connection", false)]
    public void The_guard_flags_exactly_a_process_wide_sqlite_clear(string line, bool flagged) =>
        Assert.Equal(flagged, ClearsEveryPool(line));

    private static bool ClearsEveryPool(string line) => ProcessWideClear.IsMatch(line);
}

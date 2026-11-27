using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Tests;

public sealed class OutputSwapTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "elsa3-migration-out-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, recursive: true);
    }

    [Fact]
    public void Commit_replaces_the_three_entries_and_touches_nothing_else()
    {
        Directory.CreateDirectory(Path.Combine(_out, "migrated", "workflows"));
        File.WriteAllText(Path.Combine(_out, "migrated", "workflows", "old.json"), "[]");
        File.WriteAllText(Path.Combine(_out, "report.json"), "old");
        File.WriteAllText(Path.Combine(_out, "notes.txt"), "mine");

        using (var swap = OutputSwap.Begin(_out))
        {
            Stage(swap, "new");
            swap.Commit();
        }

        Assert.Equal("new", File.ReadAllText(Path.Combine(_out, "report.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_out, "report.html")));
        Assert.True(File.Exists(Path.Combine(_out, "migrated", "workflows", "new.json")));
        Assert.False(File.Exists(Path.Combine(_out, "migrated", "workflows", "old.json")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_out, "notes.txt")));
        Assert.Empty(Directory.EnumerateDirectories(_out, OutputSwap.StagingPrefix + "*"));
    }

    [Fact]
    public void A_failed_run_removes_its_staging_folder_and_keeps_the_previous_output()
    {
        Directory.CreateDirectory(_out);
        File.WriteAllText(Path.Combine(_out, "report.json"), "old");

        using (var swap = OutputSwap.Begin(_out))
            File.WriteAllText(Path.Combine(swap.StagingFolder, "report.json"), "partial");

        Assert.Equal("old", File.ReadAllText(Path.Combine(_out, "report.json")));
        Assert.Empty(Directory.EnumerateDirectories(_out, OutputSwap.StagingPrefix + "*"));
    }

    [Fact]
    public void Commit_refuses_an_incomplete_staging_folder_and_changes_nothing()
    {
        Directory.CreateDirectory(_out);
        File.WriteAllText(Path.Combine(_out, "report.json"), "old");

        using var swap = OutputSwap.Begin(_out);
        File.WriteAllText(Path.Combine(swap.StagingFolder, "report.json"), "new");

        Assert.Throws<InvalidOperationException>(swap.Commit);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_out, "report.json")));
    }

    [Fact]
    public void Begin_creates_a_missing_output_folder()
    {
        using (var swap = OutputSwap.Begin(_out))
        {
            Stage(swap, "first");
            swap.Commit();
        }

        Assert.Equal(["migrated", "report.html", "report.json"],
            Directory.EnumerateFileSystemEntries(_out).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    private static void Stage(OutputSwap swap, string content)
    {
        Directory.CreateDirectory(Path.Combine(swap.StagingFolder, "migrated", "workflows"));
        Directory.CreateDirectory(Path.Combine(swap.StagingFolder, "migrated", "activities"));
        File.WriteAllText(Path.Combine(swap.StagingFolder, "migrated", "workflows", content + ".json"), "[]");
        File.WriteAllText(Path.Combine(swap.StagingFolder, "migrated", "activities", "activities.json"), "[]");
        File.WriteAllText(Path.Combine(swap.StagingFolder, "report.json"), content);
        File.WriteAllText(Path.Combine(swap.StagingFolder, "report.html"), content);
    }
}

using Elsa.Contracts.Generator;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>Check-mode comparison semantics (spec 149 US3, §2.23.2) over synthetic directories.</summary>
public sealed class ContractsFreshnessTests : IDisposable
{
    private readonly string committed = CreateTempDirectory();
    private readonly string regenerated = CreateTempDirectory();

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "contracts-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        Directory.Delete(committed, recursive: true);
        Directory.Delete(regenerated, recursive: true);
    }

    private void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Identical_trees_are_fresh()
    {
        Write(committed, "manifest.json", "{}");
        Write(committed, "fragments/A.json", "a");
        Write(regenerated, "manifest.json", "{}");
        Write(regenerated, "fragments/A.json", "a");

        Assert.Empty(ContractsFreshness.Compare(committed, regenerated));
    }

    [Fact]
    public void Tampered_fragment_is_reported_by_name()
    {
        Write(committed, "fragments/A.json", "stale");
        Write(regenerated, "fragments/A.json", "fresh");

        var stale = ContractsFreshness.Compare(committed, regenerated).ToList();

        Assert.Equal([Path.Combine("fragments", "A.json")], stale);
    }

    [Fact]
    public void Manifest_participates_in_the_comparison_unlike_the_maps_check()
    {
        Write(committed, "manifest.json", "{\"old\":1}");
        Write(regenerated, "manifest.json", "{\"new\":1}");

        Assert.Equal(["manifest.json"], ContractsFreshness.Compare(committed, regenerated).ToList());
    }

    [Fact]
    public void Missing_and_orphaned_files_are_both_stale()
    {
        Write(committed, "fragments/Orphaned.json", "only committed");
        Write(regenerated, "fragments/New.json", "only regenerated");

        var stale = ContractsFreshness.Compare(committed, regenerated).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.Equal(
            [Path.Combine("fragments", "New.json"), Path.Combine("fragments", "Orphaned.json")],
            stale);
    }

    [Fact]
    public void Authored_readme_is_exempt()
    {
        Write(committed, "README.md", "authored documentation");
        Write(committed, "manifest.json", "{}");
        Write(regenerated, "manifest.json", "{}");

        Assert.Empty(ContractsFreshness.Compare(committed, regenerated));
    }
}

using System.Diagnostics;
using Elsa.Maps.Projects;
using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// Spec 259: a commit's dependency map is the committed copy when the commit has one and is otherwise derived from the
/// commit's tree by the scan the maps generator runs, so the map need not be committed, and history keeps its bytes.
/// </summary>
/// <remarks>
/// The scan reads only the consolidated layout, so these histories put their projects under <c>Essentials/</c> and
/// <c>Extensions/</c>, with the lock files the scan reads pinned-transitive edges from.
/// </remarks>
public sealed class DerivedDatasetTests : IDisposable
{
    private const string Primitives = "Essentials/src/Primitives/Elsa.Primitives.csproj";
    private const string Tasks = "Essentials/src/Tasks/Elsa.Tasks.csproj";
    private const string Agent = "Extensions/Agent/src/Core/Elsa.Agent.Core.csproj";

    private readonly SyntheticRepository repo = new() { WriteLockFiles = true };

    public DerivedDatasetTests()
    {
        repo.PackageVersions["Cronos"] = "0.13.0";
        repo.PackageVersions["Microsoft.Extensions.Primitives"] = "10.0.10";
        repo.AddProject(Primitives, line: "A");
        repo.AddProject(Tasks, references: [Primitives], packages: ["Cronos"], pinnedTransitive: ["Microsoft.Extensions.Primitives"]);
        repo.AddProject(Agent, references: [Tasks]);
        repo.AddProject("Essentials/tests/Tasks/Elsa.Tasks.Tests.csproj", packable: false, references: [Tasks]);
        repo.Write("Essentials/src/Tasks/Scheduler.cs", "public sealed class Scheduler;");
        repo.Write("Essentials/src/Primitives/Primitive.cs", "public sealed class Primitive;");
        repo.Write("Extensions/Agent/src/Core/Agent.cs", "public sealed class Agent;");
    }

    [Fact]
    public void Auto_reads_the_committed_map_when_the_commit_has_one()
    {
        repo.Commit();
        // A hand edit only the committed copy has, so which source was read shows in the map.
        repo.Write(DependencyMapJson.RelativePath, repo.Read(DependencyMapJson.RelativePath).Replace("\"0.13.0\"", "\"9.9.9\"", StringComparison.Ordinal));
        var commit = repo.CommitAsIs("hand-edit the committed map");

        var auto = Snapshot(commit, DatasetSource.Auto);
        var derived = Snapshot(commit, DatasetSource.Derived);

        Assert.False(auto.MapDerived);
        Assert.Equal("9.9.9", CronosVersion(auto));
        Assert.True(derived.MapDerived);
        Assert.Equal("0.13.0", CronosVersion(derived));
    }

    [Fact]
    public void Auto_derives_the_map_when_the_commit_has_none()
    {
        var commit = CommitWithoutMap();

        var snapshot = Snapshot(commit, DatasetSource.Auto);

        Assert.True(snapshot.MapDerived);
        Assert.Equal([Primitives, Tasks, "Essentials/tests/Tasks/Elsa.Tasks.Tests.csproj", Agent], snapshot.Map.Nodes.Select(node => node.Path));
        Assert.Equal("A", snapshot.Map.ByPath[Primitives].Line);
        Assert.False(snapshot.Map.ByPath["Essentials/tests/Tasks/Elsa.Tasks.Tests.csproj"].Packable);
        Assert.Contains(snapshot.Map.ByPath[Tasks].Edges, edge => edge is { Kind: MapEdgeKind.PinnedTransitive, Id: "Microsoft.Extensions.Primitives", Version: "10.0.10" });
        Assert.Contains(snapshot.Map.ByPath[Agent].Edges, edge => edge is { Kind: MapEdgeKind.Internal, Path: Tasks });
    }

    [Fact]
    public void Committed_refuses_a_commit_without_the_map()
    {
        var commit = CommitWithoutMap();

        var exception = Assert.Throws<InvalidOperationException>(() => Snapshot(commit, DatasetSource.Committed));

        Assert.Equal($"Commit {commit} has no docs/maps/dependency-map.json; use --dataset auto or derived.", exception.Message);
    }

    /// <summary>A tree the scan refuses is refused by the calculator, naming the commit and the file, never half-read.</summary>
    [Fact]
    public void A_tree_the_scan_cannot_read_is_refused_naming_the_commit_and_the_file()
    {
        CommitWithoutMap();
        repo.WriteLockFiles = false;
        repo.PackageVersions["Cronos"] = "0.14.0";
        var commit = repo.Commit("bump Cronos without restoring");

        var exception = Assert.Throws<InvalidOperationException>(() => Snapshot(commit, DatasetSource.Auto));

        Assert.StartsWith($"Cannot derive the dependency map at {commit}: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Essentials/src/Tasks/packages.lock.json", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The configuration after the switch: the record names a commit that still carries its committed map, and the commit
    /// being built has none. A package whose inputs did not change keeps its version; the one edited advances.
    /// </summary>
    [Fact]
    public void A_derived_build_against_a_committed_record_commit_advances_only_what_changed()
    {
        var baseline = repo.Commit("baseline with its committed map");
        var record = new PublishedVersions(baseline,
        [
            new PublishedPackage("Elsa.Primitives", PackageVersionNumber.Parse("4.0.3-preview", "test"), baseline),
            new PublishedPackage("Elsa.Tasks", PackageVersionNumber.Parse("4.0.7-preview", "test"), baseline),
            new PublishedPackage("Elsa.Agent.Core", PackageVersionNumber.Parse("4.0.2-preview", "test"), baseline)
        ]);
        repo.Write("Extensions/Agent/src/Core/Agent.cs", "public sealed class Agent { }");
        var commit = CommitWithoutMap();

        var computation = VersionCalculator.Compute(repo.Calculator, commit, record);

        Assert.Equal(["Elsa.Agent.Core"], computation.Affected);
        Assert.Equal("4.0.7", computation["Elsa.Tasks"].Version.Numeric);
        Assert.Equal("4.0.3", computation["Elsa.Primitives"].Version.Numeric);
        Assert.Equal("4.0.3", computation["Elsa.Agent.Core"].Version.Numeric);
    }

    [Fact]
    public void The_cli_derives_the_map_with_dataset_derived()
    {
        var commit = repo.Commit();
        var record = Path.Join(repo.Root, ".git", "published-versions.json");
        File.WriteAllText(record, new PublishedVersions(null, []).Serialize());

        Assert.Equal(0, Run("--repo", repo.Root, "--record", record, "--commit", commit, "--output", Path.Join(repo.Root, ".git", "out.json"), "--dataset", "derived"));
        Assert.Equal(2, Run("--repo", repo.Root, "--record", record, "--commit", commit, "--dataset", "bogus"));
    }

    /// <summary>
    /// FR-003 on the real repository: the dataset derived from <c>HEAD</c>'s git objects is byte-for-byte what the maps
    /// generator writes from the working tree. A local checkout with edited or untracked project inputs has nothing to
    /// compare, since the working-tree view lists untracked files too; CI checkouts are clean.
    /// </summary>
    [Fact]
    public void The_map_derived_from_head_is_what_the_generator_writes_from_a_clean_checkout()
    {
        var workingTree = RepoContext.Discover(AppContext.BaseDirectory);
        var status = Git(workingTree.Root, "status", "--porcelain", "--", "Essentials", "Extensions", "Directory.Build.props", "Directory.Packages.props", "VersionLines.props");
        if (status.Length > 0)
            return;

        var git = new GitRepository(workingTree.Root);
        var head = git.ResolveCommit("HEAD");
        var tree = git.ListTree(head).ToDictionary(entry => entry.Path, StringComparer.Ordinal);

        Assert.Equal(DependencyMapJson.Serialize(workingTree), DependencyMapJson.Serialize(new CommitTree(new ObjectCache(git), head, tree)));
    }

    public void Dispose() => repo.Dispose();

    private string CommitWithoutMap()
    {
        repo.RegenerateMap = false;
        if (File.Exists(Path.Join(repo.Root, DependencyMapJson.RelativePath)))
            File.Delete(Path.Join(repo.Root, DependencyMapJson.RelativePath));
        return repo.Commit("no committed map");
    }

    private CommitSnapshot Snapshot(string commit, DatasetSource source) => new(new ObjectCache(repo.Calculator), commit, source);

    private static string? CronosVersion(CommitSnapshot snapshot) =>
        snapshot.Map.ByPath[Tasks].Edges.Single(edge => edge is { Kind: MapEdgeKind.External, Id: "Cronos" }).Version;

    private static int Run(params string[] arguments) =>
        (int)typeof(VersionCalculator).Assembly.EntryPoint!.Invoke(null, [arguments])!;

    private static string Git(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in (string[])["-C", root, .. arguments])
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}

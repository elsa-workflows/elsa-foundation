using System.Text;
using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// The last-published record and the dependency map as inputs: spec 150 FR-014, FR-016, FR-018, SC-012, and spec 149's
/// schema version, which the calculator enforces at read time.
/// </summary>
public sealed class RecordTests : SyntheticHistory
{
    /// <summary>
    /// SC-012, FR-016: a write-back lands on <c>publish-state</c> and changes nothing on <c>main</c>; computing the same
    /// commit against the new revision, read from the branch, finds nothing left to publish and the same fingerprints.
    /// </summary>
    [Fact]
    public void A_write_back_changes_nothing_on_main_and_leaves_nothing_to_publish()
    {
        Repo.WriteBack(Record);
        Edit("src/Tasks/Scheduler.cs");
        var commit = Repo.Commit();
        var before = Compute(commit, PublishedVersions.Load(Repo.Calculator, "publish-state"));

        Repo.WriteBack(Publish(before));
        var after = Compute(commit, PublishedVersions.Load(Repo.Calculator, "publish-state"));

        Assert.Equal(commit, Repo.Head);
        Assert.Equal(string.Empty, Repo.Git("status", "--porcelain"));
        Assert.Equal(["Elsa.Tasks"], before.Affected);
        Assert.Empty(after.Affected);
        Assert.Equal("4.0.8", VersionOf(after, "Elsa.Tasks"));
        Assert.Equal(before.Packages.Select(package => package.Fingerprint), after.Packages.Select(package => package.Fingerprint));
    }

    /// <summary>
    /// Edge case, FR-006: the record moves only for packages whose push succeeded, so a re-run of the same commit
    /// computes the same version for a package whose push failed and leaves the pushed ones alone.
    /// </summary>
    [Fact]
    public void A_package_whose_push_failed_is_retried_at_the_same_version()
    {
        Edit("src/Tasks/Scheduler.cs");
        Edit("src/Http/Http.cs");
        var computation = CommitAndCompute();
        Publish(computation, "Elsa.Tasks");

        var retry = Compute(computation.Commit);

        Assert.Equal(["Elsa.Http", "dotnet-elsa"], retry.Affected);
        Assert.Equal([VersionOf(computation, "Elsa.Http"), VersionOf(computation, "dotnet-elsa")], [VersionOf(retry, "Elsa.Http"), VersionOf(retry, "dotnet-elsa")]);
        Assert.Equal(computation["Elsa.Http"].Fingerprint, retry["Elsa.Http"].Fingerprint);
    }

    /// <summary>
    /// FR-018: the fingerprint is a digest of the package-affecting inputs alone, so it is the same at every commit
    /// where those inputs are, and changes exactly when the package does.
    /// </summary>
    [Fact]
    public void A_fingerprint_follows_the_inputs_and_nothing_else()
    {
        var baseline = Compute(Baseline);
        Repo.Write("README.md", "# Repository, revised");
        Repo.Write("src/Tasks/README.md", "# Tasks, revised");
        var unrelated = CommitAndCompute("Unrelated");
        Edit("src/Tasks/Scheduler.cs");
        var changed = CommitAndCompute();

        Assert.Equal(baseline.Packages.Select(package => package.Fingerprint), unrelated.Packages.Select(package => package.Fingerprint));
        Assert.NotEqual(baseline["Elsa.Tasks"].Fingerprint, changed["Elsa.Tasks"].Fingerprint);
        Assert.Equal(baseline["Elsa.Http"].Fingerprint, changed["Elsa.Http"].Fingerprint);
        Assert.Matches("^sha256:[0-9a-f]{64}$", changed["Elsa.Tasks"].Fingerprint);
    }

    /// <summary>FR-014: one serialization — sorted by package id, LF endings, a trailing newline — that reads back unchanged.</summary>
    [Fact]
    public void The_record_has_one_canonical_serialization()
    {
        var record = new PublishedVersions(Baseline,
        [
            new PublishedPackage("dotnet-elsa", PackageVersionNumber.Parse("4.0.5-preview", "test"), Baseline),
            new PublishedPackage("Elsa.Tasks", PackageVersionNumber.Parse("4.0.7-preview", "test"), Baseline)
        ]);

        var json = record.Serialize();

        Assert.Equal(
            $$"""
            {
              "schema_version": 1,
              "last_publish_commit": "{{Baseline}}",
              "packages": [
                {
                  "package_id": "Elsa.Tasks",
                  "version": "4.0.7-preview",
                  "commit": "{{Baseline}}"
                },
                {
                  "package_id": "dotnet-elsa",
                  "version": "4.0.5-preview",
                  "commit": "{{Baseline}}"
                }
              ]
            }

            """, json);
        Assert.Equal(json, PublishedVersions.Parse(Encoding.UTF8.GetBytes(json), "test").Serialize());
    }

    /// <summary>A record that is wrong yields wrong versions and no gate downstream can tell, so reading it is strict.</summary>
    [Theory]
    [InlineData("""{ "schema_version": 2, "last_publish_commit": null, "packages": [] }""")]
    [InlineData("""{ "schema_version": 1, "last_publish_commit": null, "packages": [ { "package_id": "Elsa.Tasks", "version": "4.0.1", "commit": "COMMIT" } ] }""")]
    [InlineData("""{ "schema_version": 1, "last_publish_commit": "COMMIT", "packages": [ { "package_id": "Elsa.Tasks", "version": "4.0.1", "commit": "COMMIT" }, { "package_id": "elsa.tasks", "version": "4.0.2", "commit": "COMMIT" } ] }""")]
    [InlineData("""{ "schema_version": 1, "last_publish_commit": "COMMIT", "packages": [ { "package_id": "Elsa.Tasks", "version": "4.0", "commit": "COMMIT" } ] }""")]
    [InlineData("""{ "schema_version": 1, "last_publish_commit": "COMMIT", "packages": [ { "package_id": "Elsa.Tasks", "version": "4.0.1", "commit": "HEAD" } ] }""")]
    [InlineData("""{ "schema_version": 1, "last_publish_commit": "COMMIT", "packages": [ { "package_id": "Elsa.Tasks", "version": "4.0.1", "commit": "COMMIT", "published_at": "2026-09-27" } ] }""")]
    [InlineData("""{ "schema_version": 1, "last_publish_commit": "COMMIT" }""")]
    public void A_malformed_record_is_refused(string json) =>
        Assert.Throws<InvalidOperationException>(() => PublishedVersions.Parse(Encoding.UTF8.GetBytes(json.Replace("COMMIT", Baseline, StringComparison.Ordinal)), "test"));

    /// <summary>Before the first publish the record is empty and names no commit; every package is new at patch 0.</summary>
    [Fact]
    public void An_empty_record_publishes_every_package_at_patch_zero()
    {
        var computation = Compute(record: PublishedVersions.Parse("""{ "schema_version": 1, "last_publish_commit": null, "packages": [] }"""u8.ToArray(), "empty"));

        Assert.Equal(AllPackages, computation.Affected);
        Assert.All(computation.Packages, package => Assert.Equal("4.0.0", package.Version.Numeric));
    }

    /// <summary>
    /// Spec 149: a dependency map with a schema version this calculator was not written for is refused, not half-read —
    /// version 1 included, which records no pinned-transitive edges and so cannot say which packages a pin reaches.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_dependency_map_with_an_unknown_schema_version_is_refused(int schemaVersion)
    {
        Repo.MapSchemaVersion = schemaVersion;
        var commit = Repo.Commit();

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(commit));

        Assert.Contains($"schema version {schemaVersion}", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record entry naming a commit whose map predates the pinned-transitive edges is refused too, rather than read as
    /// "this package reached no pin then" and compared against what it reaches now.
    /// </summary>
    [Fact]
    public void A_record_entry_whose_commit_has_a_schema_1_map_is_refused()
    {
        Repo.MapSchemaVersion = 1;
        var old = Repo.Commit("A map from before the pinned-transitive edges");
        var record = new PublishedVersions(old, Record.Packages.Select(entry => entry with { Commit = old }));
        Repo.MapSchemaVersion = 2;
        var commit = Repo.Commit("Regenerate the map");

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(commit, record));

        Assert.Contains($"at {old} has schema version 1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project added without regenerating the dependency map would own nothing and never publish; the calculator
    /// refuses a map that does not describe the commit's projects.
    /// </summary>
    [Fact]
    public void A_dependency_map_that_does_not_describe_the_tree_is_refused()
    {
        Repo.RegenerateMap = false;
        Repo.AddProject("src/Caching/Elsa.Caching.csproj");
        var commit = Repo.Commit("Add a project without regenerating the map");

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(commit));

        Assert.Contains("src/Caching/Elsa.Caching.csproj", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The output is the contract #2080 and #2082 read: its shape and ordering are pinned here.</summary>
    [Fact]
    public void The_output_lists_every_package_with_its_version_fingerprint_and_reasons()
    {
        Edit("src/Tasks/Scheduler.cs");
        var computation = CommitAndCompute();

        var json = computation.ToJson();

        Assert.StartsWith(
            $$"""
            {
              "schema_version": 1,
              "commit": "{{computation.Commit}}",
              "last_publish_commit": "{{Baseline}}",
              "lines": {
                "A": "4.0",
                "B": "4.0"
              },
              "affected": [
                "Elsa.Tasks"
              ],
              "packages": [
                {
                  "package_id": "Elsa.Events",
                  "path": "src/Events/Elsa.Events.csproj",
                  "line": "B",
                  "version": "4.0.2",
                  "affected": false,
                  "last_published": {
                    "version": "4.0.2-preview",
                    "commit": "{{Baseline}}"
                  },
                  "fingerprint": "sha256:
            """, json, StringComparison.Ordinal);
        Assert.Contains(
            """
                  "reasons": [
                    "src/Tasks/Scheduler.cs (changed)"
                  ]
            """, json, StringComparison.Ordinal);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
    }
}

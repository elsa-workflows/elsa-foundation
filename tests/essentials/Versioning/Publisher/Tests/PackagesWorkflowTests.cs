using YamlDotNet.Serialization;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// <c>.github/workflows/packages.yml</c> stays a thin shell over the publisher, and keeps spec 150's workflow-level
/// requirements: no global <c>/p:Version</c> (FR-005), no <c>--skip-duplicate</c> (FR-011), serialized runs from main that
/// are never cancelled in progress (FR-020), and pushes from main alone (FR-008).
/// </summary>
public sealed class PackagesWorkflowTests
{
    private static readonly Dictionary<object, object> Workflow = new DeserializerBuilder().Build()
        .Deserialize<Dictionary<object, object>>(File.ReadAllText(Path.Join(RepositoryRoot(), ".github", "workflows", "packages.yml")));

    private static IReadOnlyDictionary<string, Dictionary<object, object>> Jobs =>
        Map(Workflow, "jobs").ToDictionary(job => (string)job.Key, job => (Dictionary<object, object>)job.Value);

    /// <summary>Every step's script, by job.</summary>
    private static IEnumerable<(string Job, string Script)> Scripts =>
        Jobs.SelectMany(job => ((List<object>)job.Value["steps"]).Cast<Dictionary<object, object>>()
            .Where(step => step.ContainsKey("run"))
            .Select(step => (job.Key, (string)step["run"])));

    /// <summary>FR-005 and FR-011: versions come from the computation, and a duplicate is settled, never skipped.</summary>
    [Theory]
    [InlineData("p:Version")]
    [InlineData("p:PackageVersion")]
    [InlineData("--skip-duplicate")]
    public void No_step_injects_a_version_or_skips_a_duplicate(string forbidden) =>
        Assert.DoesNotContain(Scripts, script => script.Script.Contains(forbidden, StringComparison.OrdinalIgnoreCase));

    /// <summary>The publisher pushes, over the NuGet protocol; no step pushes on its own, and nothing reaches nuget.org before #2085.</summary>
    [Fact]
    public void Only_the_publisher_pushes_and_nothing_reaches_nuget_org()
    {
        Assert.DoesNotContain(Scripts, script => script.Script.Contains("nuget push", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("nuget.org", new SerializerBuilder().Build().Serialize(Workflow), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Scripts, script => script.Job == "publish" && script.Script.Contains("Elsa.Versioning.Publisher", StringComparison.Ordinal) &&
                                           script.Script.Contains(" publish ", StringComparison.Ordinal));
    }

    /// <summary>FR-020: every run from main shares one group, the one the repair workflow joins, and none is cancelled in progress.</summary>
    [Fact]
    public void Runs_from_main_are_serialized_and_never_cancelled_in_progress()
    {
        var concurrency = Map(Workflow, "concurrency");

        Assert.Contains("github.ref == 'refs/heads/main' && 'publish-state'", (string)concurrency["group"], StringComparison.Ordinal);
        Assert.Equal("false", concurrency["cancel-in-progress"]);
        Assert.All(Jobs.Values, job => Assert.False(job.ContainsKey("concurrency")));
    }

    /// <summary>
    /// FR-008 and FR-017: only the publish job holds the feed's key or may write to the repository, and it runs only for
    /// a build of main that planned a publish or the bootstrap.
    /// </summary>
    [Fact]
    public void Only_the_publish_job_holds_the_key_or_write_access_and_only_for_main()
    {
        var serializer = new SerializerBuilder().Build();
        Assert.Equal(["publish"], Jobs.Where(job => serializer.Serialize(job.Value).Contains("secrets.", StringComparison.Ordinal)).Select(job => job.Key));
        Assert.Equal(["publish"], Jobs.Where(job => job.Value.TryGetValue("permissions", out var permissions) &&
                                                   ((Dictionary<object, object>)permissions)["contents"] is "write").Select(job => job.Key));
        Assert.Equal("read", Map(Workflow, "permissions")["contents"]);

        var condition = (string)Jobs["publish"]["if"];
        Assert.Contains("github.ref == 'refs/heads/main'", condition, StringComparison.Ordinal);
        Assert.Contains("needs.pack.outputs.mode == 'publish' || needs.pack.outputs.mode == 'bootstrap'", condition, StringComparison.Ordinal);
    }

    /// <summary>The pack job builds exactly what the plan names, through the plan's own files.</summary>
    [Fact]
    public void The_pack_job_packs_through_the_plans_files()
    {
        var pack = string.Join("\n", Scripts.Where(script => script.Job == "pack").Select(script => script.Script));

        Assert.Contains(" plan ", pack, StringComparison.Ordinal);
        Assert.Contains($"/{PlanFiles.PackFilter}", pack, StringComparison.Ordinal);
        Assert.Contains($"-p:CustomBeforeDirectoryBuildProps=\"$RUNNER_TEMP/versions/{PlanFiles.PackProperties}\"", pack, StringComparison.Ordinal);
        Assert.Contains($"-p:PackageOutputPath=\"$RUNNER_TEMP/versions/{PlanFiles.Packages}\"", pack, StringComparison.Ordinal);
    }

    /// <summary>The bootstrap is the owner's manual act: a boolean dispatch input, off unless set.</summary>
    [Fact]
    public void The_bootstrap_is_a_manual_boolean_input_that_defaults_to_off()
    {
        var bootstrap = Map(Map(Map(Map(Workflow, "on"), "workflow_dispatch"), "inputs"), "bootstrap");

        Assert.Equal(("boolean", "false"), (bootstrap["type"], bootstrap["default"]));
    }

    /// <summary>A GitHub Release publishes nothing before #2085, and fails saying so rather than passing quietly.</summary>
    [Fact]
    public void A_release_publishes_nothing_and_fails_pointing_at_the_release_cut()
    {
        Assert.Equal("${{ github.event_name == 'release' }}", Jobs["release"]["if"]);
        Assert.Equal("${{ github.event_name != 'release' }}", Jobs["pack"]["if"]);
        var script = Scripts.Single(script => script.Job == "release").Script;
        Assert.Contains("#2085", script, StringComparison.Ordinal);
        Assert.Contains("exit 1", script, StringComparison.Ordinal);
    }

    private static Dictionary<object, object> Map(Dictionary<object, object> parent, string key) => (Dictionary<object, object>)parent[key];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

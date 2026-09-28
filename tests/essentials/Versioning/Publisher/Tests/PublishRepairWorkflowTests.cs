using YamlDotNet.Serialization;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// <c>.github/workflows/publish-repair.yml</c> stays a thin shell over the publisher's <c>repair</c> command, run from
/// <c>main</c> only and joined to the same <c>publish-state</c> concurrency group every run from <c>main</c> shares
/// (spec 150 FR-019, FR-020).
/// </summary>
public sealed class PublishRepairWorkflowTests
{
    private static readonly Dictionary<object, object> Workflow = new DeserializerBuilder().Build()
        .Deserialize<Dictionary<object, object>>(File.ReadAllText(Path.Join(RepositoryRoot(), ".github", "workflows", "publish-repair.yml")));

    private static IReadOnlyDictionary<string, Dictionary<object, object>> Jobs =>
        Map(Workflow, "jobs").ToDictionary(job => (string)job.Key, job => (Dictionary<object, object>)job.Value);

    private static IEnumerable<string> Scripts =>
        Jobs.Values.SelectMany(job => ((List<object>)job["steps"]).Cast<Dictionary<object, object>>())
            .Where(step => step.ContainsKey("run"))
            .Select(step => (string)step["run"]);

    /// <summary>FR-020: this run shares the group every run from main does, from its plan to its write-back, and is never cancelled.</summary>
    [Fact]
    public void The_repair_joins_the_publish_state_group_without_cancelling_a_run_in_progress()
    {
        var concurrency = Map(Workflow, "concurrency");

        Assert.Equal("publish-state", concurrency["group"]);
        Assert.Equal("false", concurrency["cancel-in-progress"]);
    }

    /// <summary>Only a manual dispatch from main runs the repair; the write-back needs this job's own write access.</summary>
    [Fact]
    public void The_repair_job_runs_from_main_only_and_holds_write_access()
    {
        Assert.Equal(["workflow_dispatch"], Map(Workflow, "on").Keys.Cast<string>());
        Assert.Equal("${{ github.ref == 'refs/heads/main' }}", Jobs["repair"]["if"]);
        Assert.Equal("write", ((Dictionary<object, object>)Jobs["repair"]["permissions"])["contents"]);
        Assert.Equal("read", Map(Workflow, "permissions")["contents"]);
    }

    /// <summary>A reason is always required; exactly one of the two modes is told apart by which input the run gives.</summary>
    [Fact]
    public void The_reason_is_required_and_the_two_modes_are_separate_inputs()
    {
        var inputs = Map(Map(Map(Workflow, "on"), "workflow_dispatch"), "inputs");

        Assert.True(((Dictionary<object, object>)inputs["reason"]).TryGetValue("required", out var required) && required.ToString() == "true");
        Assert.Equal(string.Empty, ((Dictionary<object, object>)inputs["package_id"])["default"]);
        Assert.Equal(string.Empty, ((Dictionary<object, object>)inputs["main_commit"])["default"]);
    }

    /// <summary>The one step that matters runs the publisher's repair command with the dispatch inputs and github.actor.</summary>
    [Fact]
    public void The_repair_step_runs_the_publishers_repair_command()
    {
        var script = Scripts.Single(script => script.Contains("Elsa.Versioning.Publisher", StringComparison.Ordinal) && script.Contains(" repair ", StringComparison.Ordinal));

        Assert.Contains("--package-id", script, StringComparison.Ordinal);
        Assert.Contains("--main-commit", script, StringComparison.Ordinal);
        Assert.Contains("--reason", script, StringComparison.Ordinal);
        Assert.Contains("github.actor", script, StringComparison.Ordinal);
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

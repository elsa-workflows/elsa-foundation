using YamlDotNet.Serialization;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// <c>.github/workflows/docker.yml</c> takes a main image build's package versions from the Packages workflow run
/// that produced them, never from a plan it computes itself, so it cannot race that run's plan and write-back to
/// <c>publish-state</c> (spec 150 FR-020, #2084). It also never joins the <c>publish-state</c> concurrency group -
/// GitHub keeps only one pending run per group and would silently drop a queued Packages run.
/// </summary>
public sealed class DockerWorkflowTests
{
    private static readonly Dictionary<object, object> Workflow = new DeserializerBuilder().Build()
        .Deserialize<Dictionary<object, object>>(File.ReadAllText(Path.Join(RepositoryRoot(), ".github", "workflows", "docker.yml")));

    private static IReadOnlyDictionary<string, Dictionary<object, object>> Jobs =>
        Map(Workflow, "jobs").ToDictionary(job => (string)job.Key, job => (Dictionary<object, object>)job.Value);

    /// <summary>Every step's script, by job. The reusable-workflow-call jobs have no "steps" of their own.</summary>
    private static IEnumerable<(string Job, string Script)> Scripts =>
        Jobs.Where(job => job.Value.ContainsKey("steps"))
            .SelectMany(job => ((List<object>)job.Value["steps"]).Cast<Dictionary<object, object>>()
                .Where(step => step.ContainsKey("run"))
                .Select(step => (job.Key, (string)step["run"])));

    /// <summary>A main build is triggered only by a completed Packages run, not by a push - a push would plan again and race it.</summary>
    [Fact]
    public void Main_builds_are_triggered_by_a_completed_Packages_run_not_a_push()
    {
        var on = Map(Workflow, "on");

        Assert.True(on.ContainsKey("workflow_run"));
        var workflowRun = Map(on, "workflow_run");
        Assert.Equal(["Packages"], (List<object>)workflowRun["workflows"]);
        Assert.Equal(["completed"], (List<object>)workflowRun["types"]);
        Assert.Equal(["main"], (List<object>)workflowRun["branches"]);
        Assert.False(on.ContainsKey("push"));
    }

    /// <summary>The versions job only plans for itself on a pull request; every other trigger takes the computation from a Packages run.</summary>
    [Fact]
    public void The_versions_job_only_plans_for_itself_on_a_pull_request()
    {
        var versions = Jobs["versions"];
        var condition = (string)versions["if"];

        Assert.Contains("github.event_name == 'pull_request'", condition, StringComparison.Ordinal);
        Assert.Contains("github.event.workflow_run.conclusion == 'success'", condition, StringComparison.Ordinal);

        var steps = ((List<object>)versions["steps"]).Cast<Dictionary<object, object>>().ToList();
        var planStep = steps.Single(step => step.TryGetValue("name", out var name) && (string)name == "Plan");
        Assert.Equal("github.event_name == 'pull_request'", (string)planStep["if"]);

        var downloadStep = steps.Single(step => step.TryGetValue("uses", out var uses) && ((string)uses).StartsWith("actions/download-artifact", StringComparison.Ordinal));
        Assert.Equal("github.event_name != 'pull_request'", (string)downloadStep["if"]);
        Assert.Equal("elsa-foundation-nuget-packages", (string)Map(downloadStep, "with")["name"]);
    }

    /// <summary>Neither the versions job nor any other job joins the publish-state group the Packages workflow serializes on.</summary>
    [Fact]
    public void No_job_joins_the_publish_state_concurrency_group()
    {
        var serializer = new SerializerBuilder().Build();

        Assert.False(Workflow.ContainsKey("concurrency") && serializer.Serialize(Map(Workflow, "concurrency")).Contains("publish-state", StringComparison.Ordinal));
        Assert.All(Jobs.Values, job => Assert.False(job.ContainsKey("concurrency")));
        Assert.DoesNotContain(Jobs.Values, job => serializer.Serialize(job).Contains("publish-state", StringComparison.Ordinal));
    }

    /// <summary>A resolved commit main has since moved past fails the run rather than silently building the wrong tree.</summary>
    [Fact]
    public void A_resolved_commit_main_has_moved_past_fails_the_build()
    {
        var script = Scripts.Single(script => script.Job == "versions" && script.Script.Contains("Commit drift", StringComparison.Ordinal)).Script;

        Assert.Contains("GITHUB_SHA", script, StringComparison.Ordinal);
        Assert.Contains("exit 1", script, StringComparison.Ordinal);
    }

    /// <summary>A manual dispatch on main takes the latest successful Packages run for the chosen commit, and refuses if there is none.</summary>
    [Fact]
    public void A_manual_dispatch_uses_the_latest_successful_Packages_run_and_refuses_if_none()
    {
        var dispatch = Map(Map(Workflow, "on"), "workflow_dispatch");
        var commitInput = Map(Map(dispatch, "inputs"), "commit");
        Assert.Equal("true", commitInput["required"]);

        var resolve = Scripts.Single(script => script.Job == "versions" && script.Script.Contains("workflows/packages.yml/runs", StringComparison.Ordinal)).Script;
        Assert.Contains("status=success", resolve, StringComparison.Ordinal);
        Assert.Contains("No successful Packages run", resolve, StringComparison.Ordinal);
        Assert.Contains("exit 1", resolve, StringComparison.Ordinal);
    }

    /// <summary>Downloading another run's artifact, and looking up Packages' own runs, needs actions:read - and nothing more than that plus contents:read.</summary>
    [Fact]
    public void Permissions_stay_read_only_beyond_the_reusable_builder_jobs()
    {
        var permissions = Map(Workflow, "permissions");

        Assert.Equal("read", permissions["contents"]);
        Assert.Equal("read", permissions["actions"]);
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

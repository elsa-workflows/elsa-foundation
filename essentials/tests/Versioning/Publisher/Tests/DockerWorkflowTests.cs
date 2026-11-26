using YamlDotNet.Serialization;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// <c>.github/workflows/docker.yml</c> takes a main image build's package versions from the Packages workflow run
/// that produced them, never from a plan it computes itself, so it cannot race that run's plan and write-back to
/// <c>publish-state</c> (#2084). It also never joins the <c>publish-state</c> concurrency group -
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

    /// <summary>
    /// Images are built only after a merge into main or on manual dispatch (#2751). A pull request builds no image, so a
    /// change that breaks an image build is caught by the post-merge build instead.
    /// </summary>
    [Fact]
    public void Images_build_only_after_a_merge_into_main_or_on_manual_dispatch()
    {
        var on = Map(Workflow, "on");

        Assert.False(on.ContainsKey("pull_request"), "docker.yml must not build images on pull requests (#2751); found an 'on: pull_request' trigger.");
        // The workflow_run trigger's shape (Packages, completed, main) is pinned by the test above.
        Assert.Equal(["workflow_dispatch", "workflow_run"], on.Keys.Cast<string>().Order(StringComparer.Ordinal));
        Assert.All(new[] { "build-and-push", "build-and-push-foundation-host" },
            job => Assert.Equal("true", (string)Map(Jobs[job], "with")["push"]));

        var profileMatrix = Map(Map(Jobs["package-profiles"], "strategy"), "matrix");
        Assert.Equal(["amd64", "arm64"], (List<object>)profileMatrix["arch"]);
        AssertNativeRunners("profile-builder");
        AssertNativeRunners("package-profiles");
        var build = StepScript("package-profiles", "Build the locked profile and verify offline startup");
        Assert.Contains("docker build --platform \"$PLATFORM\"", build, StringComparison.Ordinal);
        Assert.Contains("tools/docker-profiles/smoke.py \"$IMAGE\" --variant \"$PROFILE\"", build, StringComparison.Ordinal);
        var stage = StepScript("stage-profile-images", "Push the tested native images under architecture-specific tags");
        Assert.Contains("docker push \"$staged_tag\"", stage, StringComparison.Ordinal);
        var publish = StepScript("build-and-push-essentials-host", "Verify the native image pair and publish the existing release tags");
        Assert.Contains("docker buildx imagetools create -t \"$REPOSITORY:latest\" \"$source\"", publish, StringComparison.Ordinal);
    }

    /// <summary>Admission gates version resolution, which always consumes a Packages artifact and never plans for itself.</summary>
    [Fact]
    public void The_versions_job_never_plans_for_itself()
    {
        var admission = Jobs["admission"];
        var admissionSteps = ((List<object>)admission["steps"]).Cast<Dictionary<object, object>>().ToList();
        var checkoutStep = admissionSteps.Single(step => step.TryGetValue("uses", out var uses) &&
            ((string)uses).StartsWith("actions/checkout", StringComparison.Ordinal));
        Assert.Equal("${{ github.sha }}", (string)Map(checkoutStep, "with")["ref"]);

        var admitStep = admissionSteps.Single(step => step.TryGetValue("id", out var id) && (string)id == "admit");
        Assert.Equal("python3 tools/contributor-checkpoints/workflow_guards.py docker-admission", (string)admitStep["run"]);
        var admissionEnvironment = Map(admitStep, "env");
        Assert.Equal("${{ github.event.workflow_run.event }}", (string)admissionEnvironment["WORKFLOW_RUN_EVENT"]);
        Assert.Equal("${{ github.event.workflow_run.conclusion }}", (string)admissionEnvironment["WORKFLOW_RUN_CONCLUSION"]);
        Assert.Equal("${{ steps.admit.outputs.allowed }}", (string)Map(admission, "outputs")["allowed"]);

        var versions = Jobs["versions"];
        Assert.Equal(["admission"], Needs("versions"));
        Assert.Equal("needs.admission.outputs.allowed == 'true'", (string)versions["if"]);
        Assert.Equal(["versions"], Needs("build-and-push"));
        Assert.Equal(["versions"], Needs("build-and-push-foundation-host"));
        Assert.Equal(["build-and-push-foundation-host", "versions"], Needs("profile-builder"));
        Assert.Equal(["build-and-push-foundation-host", "profile-builder", "versions"], Needs("package-profiles"));
        Assert.Equal(["build-and-push-foundation-host", "package-profiles", "versions"], Needs("stage-profile-images"));
        Assert.Equal(["build-and-push-foundation-host", "stage-profile-images", "versions"], Needs("publish-profile-release"));
        Assert.Equal(["stage-profile-images", "versions"], Needs("build-and-push-essentials-host"));

        var steps = ((List<object>)versions["steps"]).Cast<Dictionary<object, object>>().ToList();
        Assert.DoesNotContain(steps, step => step.TryGetValue("name", out var name) && (string)name == "Plan");
        Assert.DoesNotContain(steps, step => step.ContainsKey("run") && ((string)step["run"]).Contains("Elsa.Versioning.Publisher", StringComparison.Ordinal));

        var downloadStep = steps.Single(step => step.TryGetValue("uses", out var uses) && ((string)uses).StartsWith("actions/download-artifact", StringComparison.Ordinal));
        Assert.False(downloadStep.ContainsKey("if"));
        Assert.Equal("elsa-foundation-nuget-packages", (string)Map(downloadStep, "with")["name"]);
        Assert.Equal("${{ steps.resolve.outputs.run-id }}", (string)Map(downloadStep, "with")["run-id"]);
        Assert.Equal("${{ steps.encode.outputs.package-versions }}", (string)Map(versions, "outputs")["package-versions"]);
    }

    [Fact]
    public void Manual_profile_dispatches_do_not_move_existing_image_tags()
    {
        Assert.Equal("github.event_name != 'workflow_dispatch' || !(inputs.build_profiles || inputs.publish_profiles)",
            Jobs["build-and-push"]["if"]);
        Assert.Equal("github.ref == 'refs/heads/main' && (inputs.publish_profiles || !inputs.build_profiles)",
            Jobs["stage-profile-images"]["if"]);
        Assert.Equal("github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main' && inputs.publish_profiles",
            Jobs["publish-profile-release"]["if"]);
        Assert.Equal("github.ref == 'refs/heads/main' && !(inputs.build_profiles || inputs.publish_profiles)",
            Jobs["build-and-push-essentials-host"]["if"]);

        var foundationTags = (string)Map(Jobs["build-and-push-foundation-host"], "with")["meta-tags"];
        Assert.Equal([
            "type=raw,value=4.0.0-preview.${{ github.run_number }}",
            "type=raw,value=latest,enable=${{ !(inputs.build_profiles || inputs.publish_profiles) }}",
            "type=sha,enable=${{ !(inputs.build_profiles || inputs.publish_profiles) }}"
        ], foundationTags.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));

        var publish = StepScript("publish-profile-release", "Publish Foundation and its builder before exposing profile tags");
        Assert.DoesNotContain(":latest", publish, StringComparison.Ordinal);
        Assert.DoesNotContain(":sha-", publish, StringComparison.Ordinal);
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

    private static IEnumerable<string> Needs(string job) => Jobs[job]["needs"] is string dependency
        ? [dependency]
        : ((List<object>)Jobs[job]["needs"]).Cast<string>().Order(StringComparer.Ordinal);

    private static string StepScript(string job, string name) => (string)((List<object>)Jobs[job]["steps"])
        .Cast<Dictionary<object, object>>()
        .Single(step => step.TryGetValue("name", out var stepName) && (string)stepName == name)["run"];

    private static void AssertNativeRunners(string job)
    {
        var definition = Jobs[job];
        Assert.Equal("${{ matrix.runner }}", definition["runs-on"]);
        var matrix = Map(Map(definition, "strategy"), "matrix");
        var runners = ((List<object>)matrix["include"]).Cast<Dictionary<object, object>>()
            .Select(entry => (Arch: (string)entry["arch"], Runner: (string)entry["runner"]))
            .OrderBy(entry => entry.Arch, StringComparer.Ordinal);
        Assert.Equal([("amd64", "ubuntu-24.04"), ("arm64", "ubuntu-24.04-arm")], runners);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

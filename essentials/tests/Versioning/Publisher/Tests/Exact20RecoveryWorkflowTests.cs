using YamlDotNet.Serialization;

namespace Elsa.Versioning.Publisher.Tests;

public sealed class Exact20RecoveryWorkflowTests
{
    private static readonly Dictionary<object, object> Workflow = new DeserializerBuilder().Build()
        .Deserialize<Dictionary<object, object>>(File.ReadAllText(Path.Join(RepositoryRoot(), ".github/workflows/publish-exact20-recovery.yml")));

    [Fact]
    public void Recovery_is_manual_and_holds_the_existing_state_lock_for_the_whole_run()
    {
        Assert.Equal(["workflow_dispatch"], Map(Workflow, "on").Keys.Cast<string>());
        var concurrency = Map(Workflow, "concurrency");
        Assert.Equal("publish-state", concurrency["group"]);
        Assert.Equal("false", concurrency["cancel-in-progress"]);
        Assert.DoesNotContain("concurrency", Map(Map(Workflow, "jobs"), "recover").Keys.Cast<string>());
    }

    [Fact]
    public void Default_preflight_has_no_write_permission_or_feed_key()
    {
        var inputs = Map(Map(Map(Workflow, "on"), "workflow_dispatch"), "inputs");
        Assert.Equal("false", Map(inputs, "execute")["default"]);
        Assert.Equal("read", Map(Workflow, "permissions")["contents"]);
        Assert.Equal("read", Map(Workflow, "permissions")["actions"]);
        var preflight = Map(Map(Workflow, "jobs"), "preflight");
        Assert.Equal("${{ github.ref == 'refs/heads/main' && !inputs.execute }}", preflight["if"]);
        Assert.False(preflight.ContainsKey("permissions"));
        var steps = Steps(preflight).ToArray();
        Assert.Equal("false", Map(steps.Single(step => step.GetValueOrDefault("uses")?.ToString() == "actions/checkout@v4"), "with")["persist-credentials"]);
        Assert.DoesNotContain(steps, step => step.TryGetValue("env", out var env) && ((Dictionary<object, object>)env).ContainsKey("FEEDZ_API_KEY"));
        var command = steps.Single(step => step.GetValueOrDefault("run")?.ToString()?.Contains("recover-exact20", StringComparison.Ordinal) == true)["run"].ToString()!;
        Assert.DoesNotContain("--execute", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_main_execution_job_receives_the_feed_key_and_state_write_permission()
    {
        var jobs = Map(Workflow, "jobs");
        Assert.Equal(["preflight", "recover"], jobs.Keys.Cast<string>());
        var recover = Map(jobs, "recover");
        Assert.Equal("${{ github.ref == 'refs/heads/main' && inputs.execute }}", recover["if"]);
        Assert.Equal("write", Map(recover, "permissions")["contents"]);
        Assert.Equal("read", Map(recover, "permissions")["actions"]);
        Assert.Equal("read", Map(recover, "permissions")["issues"]);
        Assert.Equal(["contents", "actions", "issues"], Map(recover, "permissions").Keys.Cast<string>());
        var command = Steps(recover).Single(step => step.GetValueOrDefault("run")?.ToString()?.Contains("recover-exact20", StringComparison.Ordinal) == true);
        Assert.Equal("${{ secrets.FEEDZ_API_KEY }}", Map(command, "env")["FEEDZ_API_KEY"]);
        Assert.Equal("${{ inputs.confirmation }}", Map(command, "env")["RECOVERY_CONFIRMATION"]);
        Assert.Equal("${{ inputs.approval_comment_id }}", Map(command, "env")["APPROVAL_COMMENT_ID"]);
        Assert.Contains("--execute true --confirm \"$RECOVERY_CONFIRMATION\"", command["run"].ToString(), StringComparison.Ordinal);
        Assert.Contains("--approval-comment-id \"$APPROVAL_COMMENT_ID\"", command["run"].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("${{ inputs.", command["run"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_never_rebuilds_the_original_packages_or_enables_a_publisher()
    {
        var scripts = Map(Workflow, "jobs").Values.Cast<Dictionary<object, object>>().SelectMany(Steps)
            .Where(step => step.ContainsKey("run")).Select(step => step["run"].ToString()!).ToArray();
        Assert.DoesNotContain(scripts, script => script.Contains("dotnet pack", StringComparison.Ordinal)
            || script.Contains("--skip-duplicate", StringComparison.Ordinal)
            || script.Contains("workflow enable", StringComparison.Ordinal)
            || script.Contains("--force-advance", StringComparison.Ordinal));
        Assert.All(scripts.Where(script => script.Contains("dotnet build", StringComparison.Ordinal)),
            script => Assert.Contains("tools/versioning/Elsa.Versioning.Publisher", script, StringComparison.Ordinal));
    }

    private static Dictionary<object, object> Map(Dictionary<object, object> parent, string key) => (Dictionary<object, object>)parent[key];
    private static IEnumerable<Dictionary<object, object>> Steps(Dictionary<object, object> job) => ((List<object>)job["steps"]).Cast<Dictionary<object, object>>();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

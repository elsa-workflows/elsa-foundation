using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>The resolved local clone path and the validation of the workflows path (#2197).</summary>
public sealed class GitReconciliationOptionsTests
{
    private static GitReconciliationOptions Options(string remote, string branch = "main", string cache = "") =>
        new() { RemoteUrl = remote, Branch = branch, LocalCachePath = cache };

    [Fact]
    public void The_default_clone_path_is_per_source_and_per_process_so_two_processes_never_share_a_clone()
    {
        var path = Options("git@example.com:acme/wf.git").ResolveLocalCachePath();

        Assert.Equal(Environment.ProcessId.ToString(), Path.GetFileName(path));
        Assert.StartsWith(Path.Join(Path.GetTempPath(), "elsa-gitops"), path);
        Assert.NotEqual(path, Options("git@example.com:acme/other.git").ResolveLocalCachePath());
        Assert.NotEqual(path, Options("git@example.com:acme/wf.git", "release").ResolveLocalCachePath());
    }

    [Fact]
    public void An_explicit_clone_path_is_used_as_given()
    {
        var cache = Path.Join(Path.GetTempPath(), "explicit-clone");

        Assert.Equal(cache, Options("git@example.com:acme/wf.git", cache: cache).ResolveLocalCachePath());
    }

    [Theory]
    [InlineData("workflows")]
    [InlineData("flows/prod")]
    [InlineData("workflows/")]
    [InlineData(".workflows")]
    public void A_relative_folder_is_a_valid_workflows_path(string workflowsPath) =>
        new GitReconciliationOptions { WorkflowsPath = workflowsPath }.ValidateWorkflowsPath();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("./")]
    [InlineData("../outside")]
    [InlineData("flows/../../outside")]
    [InlineData("/etc")]
    [InlineData("\\share")]
    public void A_workflows_path_that_would_aim_clean_and_restore_at_more_than_one_folder_is_refused(string workflowsPath) =>
        Assert.Throws<InvalidOperationException>(() => new GitReconciliationOptions { WorkflowsPath = workflowsPath }.ValidateWorkflowsPath());
}

using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// Edge behaviour of the working-copy manager: an unreachable remote fails fast (no interactive hang,
/// GIT_TERMINAL_PROMPT=0) and leaves no partial clone the caller would mistake for ready; and the discarding of export
/// residue never reaches past the workflows path, whatever that path holds (#2197).
/// </summary>
public sealed class GitWorkspaceTests : GitExportTest
{
    [Fact]
    public async Task Unreachable_remote_fails_fast()
    {
        var cache = GitTestSupport.NewCachePath();
        _tempPaths.Add(cache);
        var options = new GitReconciliationOptions
        {
            RemoteUrl = Path.Combine(Path.GetTempPath(), "elsa-gitops-tests", "does-not-exist-" + Guid.NewGuid().ToString("N")),
            Branch = "main", LocalCachePath = cache, Role = GitReconciliationRole.Consumer,
        };
        var workspace = GitTestSupport.Workspace(_git, GitTestSupport.Options(options));

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.EnsureReadyAsync(CancellationToken.None));
    }

    /// <summary>
    /// The feature refuses a leading ':' at registration; the workspace must hold without that check. Read as pathspecs,
    /// each of these names the whole clone, so <c>restore</c> would undo the edit and <c>clean -f -d</c> would delete the
    /// untracked files.
    /// </summary>
    [Theory]
    [InlineData(":/")]
    [InlineData(":(top)")]
    [InlineData("*")]
    public async Task A_workflows_path_holding_pathspec_magic_or_a_wildcard_leaves_everything_outside_it_alone(string workflowsPath)
    {
        var node = Writer(configure: options => options.WorkflowsPath = workflowsPath);
        await node.Source.Read(CancellationToken.None);
        var edited = Path.Join(node.CachePath, "README.md");
        var untracked = Path.Join(node.CachePath, "NOTES.md");
        var nested = Path.Join(node.CachePath, "drafts", "draft.json");
        await File.WriteAllTextAsync(edited, "edited by hand");
        await File.WriteAllTextAsync(untracked, "kept");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        await File.WriteAllTextAsync(nested, "kept");

        await node.Source.Read(CancellationToken.None); // a Writer pass discards the residue under the workflows path

        Assert.Equal("edited by hand", await File.ReadAllTextAsync(edited));
        Assert.True(File.Exists(untracked));
        Assert.True(File.Exists(nested));
        Assert.Contains(node.Git.Runs, run => run.Command == "clean");
    }
}

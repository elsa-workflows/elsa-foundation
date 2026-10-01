using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// US3 export reconciler: catalog versions absent from git are written + committed by the machine
/// identity; present ones are skipped so a second run is a no-op; Manual push leaves the remote untouched.
/// What to commit, tag and push is decided from git state, so a rerun after a stop at any point completes
/// the work (#2197).
/// </summary>
public sealed class GitWorkflowExporterTests : GitExportTest
{
    [Fact]
    public async Task Absent_versions_are_written_committed_and_idempotent()
    {
        Publish("wf-x", "X", "1.0.0", "2.0.0");
        var node = Writer();

        await node.Exporter.ExportAsync(CancellationToken.None);

        var cache = node.CachePath;
        Assert.True(File.Exists(Path.Join(cache, "workflows", "wf-x", "versions", "1.0.0.json")));
        Assert.True(File.Exists(Path.Join(cache, "workflows", "wf-x", "versions", "2.0.0.json")));
        Assert.True(File.Exists(Path.Join(cache, "workflows", "wf-x", "definition.json")));

        var subjects = Subjects(cache);
        Assert.Contains("Publish X v1.0.0 (wf-x)", subjects);
        Assert.Contains("Publish X v2.0.0 (wf-x)", subjects);

        // The written file re-hashes to the canonical hash of the serialized state.
        var fileText = await File.ReadAllTextAsync(Path.Join(cache, "workflows", "wf-x", "versions", "1.0.0.json"));
        Assert.Equal(GitCanonicalJson.Sha256Hex(GitTestSupport.EmptyStateJson), GitCanonicalJson.HashFile(fileText));

        // Machine identity authored the commits.
        Assert.Equal("Elsa Design", _git.RunOrDefault(cache, "log", "-1", "--format=%an"));

        var commitCountAfterFirst = Subjects(cache).Count;
        await node.Exporter.ExportAsync(CancellationToken.None); // second run: everything present
        Assert.Equal(commitCountAfterFirst, Subjects(cache).Count);
    }

    [Fact]
    public async Task Manual_push_mode_leaves_the_remote_untouched()
    {
        Publish("wf-m", "M", "1.0.0");
        var node = Writer(GitPushMode.Manual);

        await node.Exporter.ExportAsync(CancellationToken.None);

        // Local commit happened...
        Assert.Contains("Publish M v1.0.0 (wf-m)", Subjects(node.CachePath));
        // ...but the remote's main still has only the seed commit (nothing pushed).
        Assert.Single(RemoteSubjects());
    }

    [Theory]
    [InlineData("add", 1)]    // definition.json written, not staged
    [InlineData("commit", 1)] // definition.json staged, not committed
    [InlineData("add", 2)]    // the version file written, not staged
    [InlineData("commit", 2)] // the version file staged, not committed
    public async Task A_rerun_commits_what_a_stop_after_the_write_left_uncommitted(string command, int occurrence)
    {
        Publish("wf-c", "C", "1.0.0");
        var node = Writer();
        node.Git.FailAt(command, occurrence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.Exporter.ExportAsync(CancellationToken.None));

        await node.Exporter.ExportAsync(CancellationToken.None);

        Assert.True(IsCommitted(node.CachePath, "workflows/wf-c/definition.json"));
        Assert.True(IsCommitted(node.CachePath, "workflows/wf-c/versions/1.0.0.json"));
        Assert.Contains("Publish C v1.0.0 (wf-c)", Subjects(node.CachePath));
        Assert.Empty(_git.RunOrDefault(node.CachePath, "status", "--porcelain"));
    }

    [Fact]
    public async Task A_rerun_commits_a_metadata_change_a_stop_left_staged()
    {
        Publish("wf-n", "Before", "1.0.0");
        var node = Writer();
        await node.Exporter.ExportAsync(CancellationToken.None);
        (await _definitions.GetAsync("wf-n")).Name = "After";
        node.Git.FailAt("commit", occurrence: 3); // the two commits of the first pass, then the metadata change's
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.Exporter.ExportAsync(CancellationToken.None));

        await node.Exporter.ExportAsync(CancellationToken.None);

        Assert.Contains("\"After\"", _git.RunOrDefault(node.CachePath, "show", "HEAD:workflows/wf-n/definition.json"));
        Assert.Contains("Update metadata After (wf-n)", Subjects(node.CachePath));
        Assert.Empty(_git.RunOrDefault(node.CachePath, "status", "--porcelain"));
    }

    [Fact]
    public async Task A_rerun_tags_a_version_a_stop_after_its_commit_left_untagged()
    {
        Publish("wf-t", "T", "1.0.0");
        var node = Writer();
        node.Git.FailAt("tag", occurrence: 2); // the first is the listing of the tags already there
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.Exporter.ExportAsync(CancellationToken.None));
        Assert.Contains("Publish T v1.0.0 (wf-t)", Subjects(node.CachePath));
        Assert.Empty(_git.RunOrDefault(node.CachePath, "tag", "--list", "wf/*"));

        await node.Exporter.ExportAsync(CancellationToken.None);

        var added = _git.RunOrDefault(node.CachePath, "log", "-1", "--format=%H", "--diff-filter=A", "--", "workflows/wf-t/versions/1.0.0.json");
        Assert.NotEmpty(added);
        Assert.Equal(added, Head(node.CachePath, "wf/wf-t/v1.0.0^{commit}"));
    }

    [Fact]
    public async Task A_failed_push_is_retried_on_the_next_pass_though_it_commits_nothing()
    {
        Publish("wf-p", "P", "1.0.0");
        var node = Writer(GitPushMode.Immediate);
        node.Git.FailAt("push");
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.Exporter.ExportAsync(CancellationToken.None));
        Assert.DoesNotContain("Publish P v1.0.0 (wf-p)", RemoteSubjects());
        var commits = Subjects(node.CachePath).Count;

        await node.Exporter.ExportAsync(CancellationToken.None);

        Assert.Equal(commits, Subjects(node.CachePath).Count);
        Assert.Contains("Publish P v1.0.0 (wf-p)", RemoteSubjects());
        Assert.Equal(Head(_remote, "main"), Head(node.CachePath));
    }
}

using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// Two Writer nodes of one catalog, each exporting from a clone of its own, as replicas that share a configuration do
/// (#2197). The push is the fence: only one writer's commits reach the remote, the other rebuilds on them, and neither
/// node's start fails. A commit the export did not make is never discarded to get there.
/// </summary>
public sealed class GitWorkflowExportConvergenceTests : GitExportTest
{
    private readonly GitWriterNode _first;
    private readonly GitWriterNode _second;

    public GitWorkflowExportConvergenceTests()
    {
        _first = Writer(GitPushMode.Immediate);
        _second = Writer(GitPushMode.Immediate);
    }

    [Fact]
    public async Task Two_writers_racing_to_push_land_one_writers_commits_and_the_other_rebuilds_on_them()
    {
        Publish("wf-r", "R", "1.0.0");
        // The first node exports and pushes in the moment between the second node's commits and its push.
        _second.Git.Before("push", 1, () => _first.Exporter.ExportAsync(CancellationToken.None));

        await _second.Exporter.ExportAsync(CancellationToken.None);

        Assert.Single(RemoteSubjects(), subject => subject == "Publish R v1.0.0 (wf-r)");
        Assert.Equal(Head(_remote, "main"), Head(_first.CachePath));
        Assert.Equal(Head(_remote, "main"), Head(_second.CachePath));
        Assert.Contains(_second.ExportLog.Entries, entry => entry.Message.Contains("another writer pushed first"));
    }

    [Fact]
    public async Task A_writer_whose_unpushed_export_lost_to_another_writer_resets_to_the_remote_on_its_next_start()
    {
        Publish("wf-a", "A", "1.0.0");
        await _second.Source.Read(CancellationToken.None);
        _second.Git.FailAt("push");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _second.Exporter.ExportAsync(CancellationToken.None));
        await _first.Exporter.ExportAsync(CancellationToken.None);

        // The second node starts again: the import task, then the export task. Neither may throw.
        var imported = await _second.Source.Read(CancellationToken.None);
        await _second.Exporter.ExportAsync(CancellationToken.None);

        Assert.Contains(imported, model => model is { DefinitionId: "wf-a", Version: "1.0.0" });
        Assert.Single(RemoteSubjects(), subject => subject == "Publish A v1.0.0 (wf-a)");
        Assert.Equal(Head(_remote, "main"), Head(_second.CachePath));
        Assert.Contains(_second.WorkspaceLog.Warnings, warning => warning.Message.Contains("unpushed export commit(s) are discarded"));
        // The tag the second node made on its discarded commit now marks the commit that reached the remote.
        Assert.Equal(
            _git.RunOrDefault(_second.CachePath, "log", "-1", "--format=%H", "--diff-filter=A", "--", "workflows/wf-a/versions/1.0.0.json"),
            Head(_second.CachePath, "wf/wf-a/v1.0.0^{commit}"));
    }

    [Fact]
    public async Task A_diverged_clone_holding_a_commit_the_export_did_not_make_is_kept_and_still_starts()
    {
        Publish("wf-f", "F", "1.0.0");
        await _second.Source.Read(CancellationToken.None);
        var operatorCommit = await CommitByHandAsync(_second.CachePath);
        await _first.Exporter.ExportAsync(CancellationToken.None);

        await _second.Source.Read(CancellationToken.None);
        await _second.Exporter.ExportAsync(CancellationToken.None);

        Assert.Equal(operatorCommit, _git.RunOrDefault(_second.CachePath, "merge-base", operatorCommit, "HEAD"));
        Assert.Contains(_second.WorkspaceLog.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("operator@example.com"));
        Assert.DoesNotContain("Operator note", RemoteSubjects());
        Assert.Contains(_second.ExportLog.Warnings, warning => warning.Message.Contains("were not pushed"));
    }

    [Fact]
    public async Task A_writer_moving_to_the_remote_keeps_uncommitted_changes_outside_the_workflows_path()
    {
        Publish("wf-k", "K", "1.0.0");
        await _second.Source.Read(CancellationToken.None);
        var readme = Path.Join(_second.CachePath, "README.md");
        await File.WriteAllTextAsync(readme, "edited by hand");
        await _first.Exporter.ExportAsync(CancellationToken.None);

        await _second.Exporter.ExportAsync(CancellationToken.None);

        Assert.Equal(Head(_remote, "main"), Head(_second.CachePath));
        Assert.Equal("edited by hand", await File.ReadAllTextAsync(readme));
    }

    private async Task<string> CommitByHandAsync(string repository)
    {
        await File.WriteAllTextAsync(Path.Join(repository, "NOTES.md"), "kept");
        await _git.RunAsync(repository, CancellationToken.None, "add", "--", "NOTES.md");
        await _git.RunAsync(repository, CancellationToken.None,
            "-c", "user.name=Operator", "-c", "user.email=operator@example.com", "commit", "-m", "Operator note");
        return Head(repository);
    }
}

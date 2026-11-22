using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
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

    [Fact]
    public async Task A_writer_that_loses_every_push_stops_after_its_attempts_without_failing_and_keeps_its_commits()
    {
        Publish("wf-e", "E", "1.0.0");
        // Another writer moves the remote just before each of the attempts, so the rebuilt clone is behind again each time.
        for (var push = 1; push <= GitWorkflowExporter.MaxPushAttempts; push++)
            _second.Git.Before("push", push, () => AdvanceRemoteAsync("main"));

        await _second.Exporter.ExportAsync(CancellationToken.None);

        var refused = Assert.Single(_second.ExportLog.Warnings);
        Assert.Contains($"other writers moved it before each of {GitWorkflowExporter.MaxPushAttempts} pushes", refused.Message);
        Assert.Contains("Publish E v1.0.0 (wf-e)", Subjects(_second.CachePath));
        Assert.DoesNotContain("Publish E v1.0.0 (wf-e)", RemoteSubjects());
    }

    [Fact]
    public async Task A_move_that_would_overwrite_an_uncommitted_change_keeps_the_clone_and_still_starts_reporting_once()
    {
        Publish("wf-u", "U", "1.0.0");
        await _second.Source.Read(CancellationToken.None);
        var readme = Path.Join(_second.CachePath, "README.md");
        await File.WriteAllTextAsync(readme, "edited by hand");
        await AdvanceRemoteAsync("main"); // rewrites README.md, which the clone cannot move onto without losing the edit

        await _second.Source.Read(CancellationToken.None);
        await _second.Exporter.ExportAsync(CancellationToken.None);

        Assert.Equal("edited by hand", await File.ReadAllTextAsync(readme));
        Assert.Single(_second.WorkspaceLog.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("would overwrite an uncommitted change"));
        var warning = Assert.Single(_second.ExportLog.Warnings);
        Assert.Contains("could not be moved onto it", warning.Message);
        Assert.DoesNotContain("before each of", warning.Message);
        Assert.Contains("Publish U v1.0.0 (wf-u)", Subjects(_second.CachePath));
        Assert.DoesNotContain("Publish U v1.0.0 (wf-u)", RemoteSubjects());
    }

    [Fact]
    public async Task A_diverged_clone_holding_a_foreign_commit_reports_once_across_the_rebuild_attempts()
    {
        Publish("wf-o", "O", "1.0.0");
        await _second.Source.Read(CancellationToken.None);
        await CommitByHandAsync(_second.CachePath);
        await _first.Exporter.ExportAsync(CancellationToken.None);

        await _second.Source.Read(CancellationToken.None);
        await _second.Exporter.ExportAsync(CancellationToken.None);

        Assert.Single(_second.WorkspaceLog.Entries, entry => entry.Level == LogLevel.Error);
        var warning = Assert.Single(_second.ExportLog.Warnings);
        Assert.Contains("could not be moved onto it", warning.Message);
        Assert.DoesNotContain("before each of", warning.Message);
    }

    [Theory]
    [InlineData("Elsa Design", "design@elsa.local", "human@example.com", "committed by human@example.com")] // an amend or rebase changes the committer
    [InlineData("Someone Else", "design@elsa.local", "design@elsa.local", "Someone Else")]                  // a different author name
    [InlineData("Elsa Design", "human@example.com", "design@elsa.local", "<human@example.com>")]             // a different author email
    public async Task A_commit_under_the_export_identity_that_a_person_amended_is_not_discarded(
        string authorName, string authorEmail, string committerEmail, string reported)
    {
        Publish("wf-h", "H", "1.0.0");
        await _second.Exporter.ExportAsync(CancellationToken.None);
        await _git.RunAsync(_second.CachePath, CancellationToken.None,
            "-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}", "-c", $"committer.email={committerEmail}",
            "commit", "--amend", "--no-edit", "--allow-empty", "--reset-author");
        var amended = Head(_second.CachePath);
        await AdvanceRemoteAsync("main");

        await _second.Source.Read(CancellationToken.None);

        Assert.Equal(amended, Head(_second.CachePath));
        Assert.Single(_second.WorkspaceLog.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains(reported));
    }

    [Fact]
    public async Task A_divergence_that_comes_back_after_a_repair_is_reported_again()
    {
        Publish("wf-g", "G", "1.0.0");
        await _second.Source.Read(CancellationToken.None);
        await CommitByHandAsync(_second.CachePath);
        await _first.Exporter.ExportAsync(CancellationToken.None);
        await _second.Source.Read(CancellationToken.None);
        await _second.Source.Read(CancellationToken.None); // still diverged: not reported again

        // The operator reconciles the clone with the remote by hand; the next pass finds it healthy.
        await _git.RunAsync(_second.CachePath, CancellationToken.None, "reset", "--hard", "refs/remotes/origin/main");
        await _second.Source.Read(CancellationToken.None);
        // Then it diverges again.
        await CommitByHandAsync(_second.CachePath);
        await AdvanceRemoteAsync("main");
        await _second.Source.Read(CancellationToken.None);

        Assert.Equal(2, _second.WorkspaceLog.Entries.Count(entry => entry.Level == LogLevel.Error && entry.Message.Contains("operator@example.com")));
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

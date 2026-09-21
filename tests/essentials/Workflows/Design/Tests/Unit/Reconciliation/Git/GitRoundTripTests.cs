using Elsa.Git;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// US4 coherence: an exported version re-imports to the same identity (so a reconciler holding it skips
/// it — a round-trip no-op), and the single-writer gate refuses a non-fast-forward push (never forced).
/// The import-side hash tripwire (US4 #3) is covered by the reconciler's
/// <c>Same_id_version_different_content_logs_warning</c> unit test.
/// </summary>
public sealed class GitRoundTripTests : GitIntegrationTest
{
    [Fact]
    public async Task Exported_version_reimports_to_the_same_identity_and_hash()
    {
        var remote = GitTestSupport.CreateRemoteWithMain(_git, _tempPaths);
        var writerCache = GitTestSupport.NewCachePath();
        _tempPaths.Add(writerCache);

        // Writer authors + exports wf-r v1.0.0.
        var writerOptions = new GitReconciliationOptions
        {
            RemoteUrl = remote, Branch = "main", WorkflowsPath = "workflows",
            LocalCachePath = writerCache, Role = GitReconciliationRole.Writer,
        };
        var writerOpts = GitTestSupport.Options(writerOptions);
        var writerWorkspace = new GitWorkspace(_git, writerOpts, NullLogger<GitWorkspace>.Instance);
        var defs = new InMemoryDefinitionStore().With(new WorkflowDefinition { Id = "wf-r", Name = "R" });
        var versions = new InMemoryVersionStore().With(new WorkflowDefinitionVersion("wf-r", "1.0.0") { State = WorkflowDefinitionState.Empty });
        var exporter = new GitWorkflowExporter(writerWorkspace, _git, new FakePayloadSerializer(), defs, versions, writerOpts, NullLogger<GitWorkflowExporter>.Instance);

        await exporter.ExportAsync(CancellationToken.None);

        // A Consumer importing the writer's repo sees exactly that version, with the canonical hash.
        var consumerCache = GitTestSupport.NewCachePath();
        _tempPaths.Add(consumerCache);
        var consumerOptions = new GitReconciliationOptions
        {
            RemoteUrl = writerCache, Branch = "main", WorkflowsPath = "workflows",
            LocalCachePath = consumerCache, Role = GitReconciliationRole.Consumer,
        };
        var consumerOpts = GitTestSupport.Options(consumerOptions);
        var consumerWorkspace = new GitWorkspace(_git, consumerOpts, NullLogger<GitWorkspace>.Instance);
        var source = new GitWorkflowReconciliationSource(consumerWorkspace, _git, new FakePayloadSerializer(), consumerOpts, NullLogger<GitWorkflowReconciliationSource>.Instance);

        var model = Assert.Single(await source.Read(CancellationToken.None));
        Assert.Equal("wf-r", model.DefinitionId);
        Assert.Equal("1.0.0", model.Version);
        Assert.Equal(GitCanonicalJson.Sha256Hex(GitTestSupport.EmptyStateJson), model.ContentHash);
        // (id, version) already in the catalog ⇒ the reconciler dedups it ⇒ import is a no-op.

        var subjectsBefore = _git.RunOrDefault(writerCache, "log", "--format=%s");
        await exporter.ExportAsync(CancellationToken.None); // re-export: structural no-op
        Assert.Equal(subjectsBefore, _git.RunOrDefault(writerCache, "log", "--format=%s"));
    }

    [Fact]
    public async Task Non_fast_forward_push_is_refused()
    {
        var remote = GitTestSupport.CreateRemoteWithMain(_git, _tempPaths);
        var cloneA = await CloneFromMain(remote);
        var cloneB = await CloneFromMain(remote);

        // Writer A pushes first — fast-forward, accepted.
        await CommitFile(cloneA, "a.txt", "A");
        await _git.RunAsync(cloneA, CancellationToken.None, "push", "origin", "main");

        // Writer B committed on the same base; its push is now a non-fast-forward and must be refused
        // (no --force) — the D7 single-writer gate.
        await CommitFile(cloneB, "b.txt", "B");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _git.RunAsync(cloneB, CancellationToken.None, "push", "origin", "main"));
    }

    private async Task<string> CloneFromMain(string remote)
    {
        var path = GitTestSupport.NewCachePath();
        _tempPaths.Add(path);
        var parent = Directory.GetParent(path)!.FullName;
        Directory.CreateDirectory(parent);
        await _git.RunAsync(parent, CancellationToken.None, "clone", "--branch", "main", remote, path);
        await _git.RunAsync(path, CancellationToken.None, "config", "user.email", "w@elsa.local");
        await _git.RunAsync(path, CancellationToken.None, "config", "user.name", "W");
        return path;
    }

    private async Task CommitFile(string repo, string name, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(repo, name), content);
        await _git.RunAsync(repo, CancellationToken.None, "add", "--", name);
        await _git.RunAsync(repo, CancellationToken.None, "commit", "-m", $"add {name}");
    }
}

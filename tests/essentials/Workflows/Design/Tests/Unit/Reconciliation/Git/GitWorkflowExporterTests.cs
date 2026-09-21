using Elsa.Git;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// US3 export reconciler: catalog versions absent from git are written + committed by the machine
/// identity; present ones are skipped so a second run is a no-op; Manual push leaves the remote untouched.
/// </summary>
public sealed class GitWorkflowExporterTests : GitIntegrationTest
{
    private (GitWorkflowExporter Exporter, string CachePath) NewExporter(
        InMemoryDefinitionStore defs, InMemoryVersionStore versions, GitPushMode pushMode = GitPushMode.Manual)
    {
        var remote = GitTestSupport.CreateRemoteWithMain(_git, _tempPaths);
        var cachePath = GitTestSupport.NewCachePath();
        _tempPaths.Add(cachePath);

        var options = new GitReconciliationOptions
        {
            RemoteUrl = remote, Branch = "main", WorkflowsPath = "workflows",
            LocalCachePath = cachePath, Role = GitReconciliationRole.Writer,
            Export = new GitExportOptions { PushMode = pushMode, Tag = true },
        };
        var opts = GitTestSupport.Options(options);
        var workspace = new GitWorkspace(_git, opts, NullLogger<GitWorkspace>.Instance);
        var exporter = new GitWorkflowExporter(workspace, _git, new FakePayloadSerializer(), defs, versions, opts, NullLogger<GitWorkflowExporter>.Instance);
        return (exporter, cachePath);
    }

    private static WorkflowDefinitionVersion Version(string defId, string version) =>
        new(defId, version) { State = WorkflowDefinitionState.Empty };

    private IReadOnlyList<string> CommitSubjects(string repoPath) =>
        _git.RunOrDefault(repoPath, "log", "--format=%s").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Absent_versions_are_written_committed_and_idempotent()
    {
        var defs = new InMemoryDefinitionStore().With(new WorkflowDefinition { Id = "wf-x", Name = "X" });
        var versions = new InMemoryVersionStore().With(Version("wf-x", "1.0.0")).With(Version("wf-x", "2.0.0"));
        var (exporter, cache) = NewExporter(defs, versions);

        await exporter.ExportAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(cache, "workflows", "wf-x", "versions", "1.0.0.json")));
        Assert.True(File.Exists(Path.Combine(cache, "workflows", "wf-x", "versions", "2.0.0.json")));
        Assert.True(File.Exists(Path.Combine(cache, "workflows", "wf-x", "definition.json")));

        var subjects = CommitSubjects(cache);
        Assert.Contains("Publish X v1.0.0 (wf-x)", subjects);
        Assert.Contains("Publish X v2.0.0 (wf-x)", subjects);

        // The written file re-hashes to the canonical hash of the serialized state.
        var fileText = await File.ReadAllTextAsync(Path.Combine(cache, "workflows", "wf-x", "versions", "1.0.0.json"));
        Assert.Equal(GitCanonicalJson.Sha256Hex(GitTestSupport.EmptyStateJson), GitCanonicalJson.HashFile(fileText));

        // Machine identity authored the commits.
        Assert.Equal("Elsa Design", _git.RunOrDefault(cache, "log", "-1", "--format=%an"));

        var commitCountAfterFirst = CommitSubjects(cache).Count;
        await exporter.ExportAsync(CancellationToken.None); // second run: everything present
        Assert.Equal(commitCountAfterFirst, CommitSubjects(cache).Count);
    }

    [Fact]
    public async Task Manual_push_mode_leaves_the_remote_untouched()
    {
        var defs = new InMemoryDefinitionStore().With(new WorkflowDefinition { Id = "wf-m", Name = "M" });
        var versions = new InMemoryVersionStore().With(Version("wf-m", "1.0.0"));
        var (exporter, cache) = NewExporter(defs, versions, GitPushMode.Manual);

        await exporter.ExportAsync(CancellationToken.None);

        // Local commit happened...
        Assert.Contains("Publish M v1.0.0 (wf-m)", CommitSubjects(cache));
        // ...but the remote's main still has only the seed commit (nothing pushed).
        var remotePath = _git.RunOrDefault(cache, "remote", "get-url", "origin");
        Assert.Single(_git.RunOrDefault(remotePath, "log", "--format=%s", "main").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}

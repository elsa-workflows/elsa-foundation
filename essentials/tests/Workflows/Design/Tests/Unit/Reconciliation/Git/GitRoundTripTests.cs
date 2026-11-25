using Elsa.Git;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// Round-trip coherence: an exported version re-imports to the same identity (so a reconciler holding it skips
/// it — a round-trip no-op), and the single-writer gate refuses a non-fast-forward push (never forced).
/// The import-side hash check is covered by the reconciler's
/// <c>Same_id_version_different_content_logs_warning</c> unit test.
/// </summary>
public sealed class GitRoundTripTests : GitIntegrationTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exported_version_reimports_to_the_same_identity_and_hash(bool realSerializer)
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
        var writerWorkspace = GitTestSupport.Workspace(_git, writerOpts);
        var defs = new InMemoryDefinitionStore().With(new WorkflowDefinition { Id = "wf-r", Name = "R" });
        var state = realSerializer ? GitTestSupport.AuthoredStateFixture : WorkflowDefinitionState.Empty;
        IPayloadSerializer serializer = realSerializer ? SerializationServices.PayloadSerializer() : new FakePayloadSerializer();
        var versions = new InMemoryVersionStore().With(new WorkflowDefinitionVersion("wf-r", "1.0.0") { State = state });
        var exporter = new GitWorkflowExporter(
            writerWorkspace, _git, serializer, defs, versions, writerOpts,
            CredentialLiteralTestSupport.Validator(CredentialLiteralTestSupport.Catalog()), NullLogger<GitWorkflowExporter>.Instance);

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
        var consumerWorkspace = GitTestSupport.Workspace(_git, consumerOpts);
        var source = new GitWorkflowReconciliationSource(consumerWorkspace, _git, serializer, consumerOpts, NullLogger<GitWorkflowReconciliationSource>.Instance);

        var model = Assert.Single(await source.Read(CancellationToken.None));
        Assert.Equal("wf-r", model.DefinitionId);
        Assert.Equal("1.0.0", model.Version);
        Assert.Equal(GitCanonicalJson.Sha256Hex(serializer.Serialize(state)), model.ContentHash);
        if (realSerializer)
        {
            var variable = Assert.Single(model.State.Variables);
            Assert.Equal("FixtureName", variable.Name);
            Assert.Equal(GitTestSupport.AuthoredRawFixtureValue, variable.Default!.Value!.Value.GetRawText());
            var exported = await File.ReadAllTextAsync(Path.Combine(writerCache, "workflows", "wf-r", "versions", "1.0.0.json"));
            using var document = JsonDocument.Parse(exported);
            Assert.True(document.RootElement.TryGetProperty("variables", out var variables));
            Assert.False(document.RootElement.TryGetProperty("Variables", out _));
            Assert.Equal("FixtureName", variables[0].GetProperty("name").GetString());
            Assert.Equal(GitTestSupport.AuthoredRawFixtureValue, variables[0].GetProperty("default").GetProperty("value").GetRawText());
        }
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

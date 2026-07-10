using System.Text.Json;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowExecutableStoreTests
{
    // The same contract assertions run against two host-selected providers (real Groundwork SQLite and
    // an in-memory document store). Identical behavior proves the executable bridge is provider-neutral,
    // and round-tripping a nested node tree proves the full executable graph survives serialization.
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RoundTrips_Across_Providers(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Executable("artifact-1"));
        await store.SaveAsync(Executable("artifact-2"));

        var found = await store.FindAsync("artifact-1");
        Assert.NotNull(found);
        Assert.Equal("artifact-1", found!.Identity.ArtifactId);
        Assert.Equal("definition-1", found.Identity.DefinitionId);
        Assert.Equal("hash-artifact-1", found.Identity.ArtifactHash);

        // Nested tree survives: root + child slot + child node.
        Assert.Equal("root", found.RootActivity.ExecutableNodeId);
        Assert.Equal("Elsa.Sequence", found.RootActivity.ActivityType);
        var slot = Assert.Single(found.RootActivity.ChildSlots);
        Assert.Equal("Body", slot.Name);
        var child = Assert.Single(slot.Activities);
        Assert.Equal("child", child.ExecutableNodeId);

        // Compiled input binding survives.
        var binding = child.InputBindings["to"];
        Assert.Equal(RuntimeInputBindingSource.DurableValue, binding.Source);
        Assert.Equal("customerEmail", binding.DurableValue!.ValueId);

        // Raw descriptor payload survives as JSON.
        Assert.Equal("Send", found.RootActivity.DescriptorPayload.GetProperty("kind").GetString());

        // Resume targets and recomputed projections survive.
        Assert.Equal("node-child", found.ResumeTargets["resume-1"].ExecutableNodeId);
        Assert.Equal(2, found.Nodes.Count);
        Assert.True(found.NodesById.ContainsKey("child"));
        Assert.Equal("slice-1", found.CompatibilityMetadata["slice"]);

        var all = await store.ListAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { "artifact-1", "artifact-2" }, all.Select(x => x.Identity.ArtifactId).OrderBy(x => x));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_Is_Idempotent_By_ArtifactId(string provider)
    {
        // ADR 0038: artifacts are content-addressed and immutable. A second save under the same artifact id (a
        // behaviorally identical republish) leaves the existing artifact untouched rather than overwriting it.
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Executable("artifact-1", artifactVersion: "1"));
        await store.SaveAsync(Executable("artifact-1", artifactVersion: "2"));

        var found = await store.FindAsync("artifact-1");
        Assert.Equal("1", found!.Identity.ArtifactVersion);
        Assert.Single(await store.ListAsync());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Find_Returns_Null_When_Absent(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        Assert.Null(await store.FindAsync("missing"));
        Assert.Empty(await store.ListAsync());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_Removes_Artifact(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Executable("artifact-1"));
        Assert.True(await store.DeleteAsync("artifact-1"));
        Assert.Null(await store.FindAsync("artifact-1"));
        Assert.Empty(await store.ListAsync());
        Assert.False(await store.DeleteAsync("artifact-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SourceReferenceStore_RoundTrips_And_Filters(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkWorkflowExecutableSourceReferenceStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published, publishedAt: now));
        await store.SaveAsync(Reference("ref-2", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: now.AddMinutes(30)));
        await store.SaveAsync(Reference("ref-3", "artifact-2", WorkflowExecutableReferenceScope.TestRun, expiresAt: now.AddMinutes(-1)));

        var byArtifact = await store.ListByArtifactAsync("artifact-1");
        Assert.Equal(new[] { "ref-1", "ref-2" }, byArtifact.Select(r => r.SourceReferenceId).OrderBy(x => x));

        var live = await store.ListAsync(liveOnly: true, now: now);
        Assert.Equal(new[] { "ref-1", "ref-2" }, live.Select(r => r.SourceReferenceId).OrderBy(x => x));

        var published = await store.ListAsync(scope: WorkflowExecutableReferenceScope.Published, now: now);
        Assert.Equal("ref-1", Assert.Single(published).SourceReferenceId);

        // Layout sidecar survives the round-trip.
        var reloaded = await store.FindAsync("ref-1");
        Assert.Equal("node-a", Assert.Single(reloaded!.Layout).NodeId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SourceReferenceStore_Retire_Expiry_And_Unreferenced_Primitives(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkWorkflowExecutableSourceReferenceStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Reference("ref-live", "artifact-1", WorkflowExecutableReferenceScope.Published, publishedAt: now));
        await store.SaveAsync(Reference("ref-expired", "artifact-2", WorkflowExecutableReferenceScope.TestRun, expiresAt: now.AddMinutes(-1)));
        await store.SaveAsync(Reference("ref-to-retire", "artifact-3", WorkflowExecutableReferenceScope.Published, publishedAt: now));

        Assert.True(await store.RetireAsync("ref-to-retire", now, "manual"));
        var retired = await store.FindAsync("ref-to-retire");
        Assert.Equal(now, retired!.DeletedAt);
        Assert.Equal("manual", retired.DeletedReason);

        var swept = await store.DeleteExpiredOrRetiredAsync(now);
        Assert.Equal(new[] { "ref-expired", "ref-to-retire" }, swept.OrderBy(x => x));
        Assert.Null(await store.FindAsync("ref-expired"));

        var unreferenced = await store.ListUnreferencedArtifactIdsAsync(["artifact-1", "artifact-2", "artifact-3"], now);
        // artifact-1 still has a live reference; 2 and 3 lost theirs to the sweep.
        Assert.Equal(new[] { "artifact-2", "artifact-3" }, unreferenced.OrderBy(x => x));
    }

    [Fact]
    public void Serialization_Omits_Derived_Node_Projections()
    {
        var json = GroundworkTestSerialization.Serializer.SerializeForComparison(Executable("artifact-1"));

        // RootActivity is the single source of truth; Nodes/NodesById are rebuilt on load (asserted in
        // RoundTrips_Across_Providers) and must not be persisted.
        Assert.Contains("\"rootActivity\"", json);
        Assert.DoesNotContain("\"nodesById\"", json);
        Assert.DoesNotContain("\"nodes\"", json);
    }

    [Fact]
    public async Task Published_Executable_Survives_Restart()
    {
        // DS-2 (durability): the production publish path persists compiled executables through
        // GroundworkWorkflowExecutableStore. A published artifact must survive a host restart — proven by
        // writing through one bridge instance over a file-backed SQLite database, disposing it, then reopening
        // the SAME database file with a FRESH bridge and reading the artifact (and its node tree) back.
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-executable-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                await store.SaveAsync(Executable("artifact-1"));
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

                var found = await store.FindAsync("artifact-1");
                Assert.NotNull(found);
                Assert.Equal("artifact-1", found!.Identity.ArtifactId);
                Assert.Equal("definition-1", found.Identity.DefinitionId);
                Assert.Equal("root", found.RootActivity.ExecutableNodeId);
                Assert.Equal("child", Assert.Single(Assert.Single(found.RootActivity.ChildSlots).Activities).ExecutableNodeId);
                Assert.Equal("artifact-1", Assert.Single(await store.ListAsync()).Identity.ArtifactId);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static WorkflowExecutable Executable(string artifactId, string artifactVersion = "1")
    {
        var child = new ExecutableNode(
            executableNodeId: "child",
            authoredActivityId: "authored-child",
            activityType: "Elsa.SendEmail",
            activityTypeVersion: "1.0.0",
            descriptorType: "Elsa.Activities.SendEmailDescriptor",
            descriptorPayload: Json("""{ "kind": "Send" }"""),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["to"] = new(
                    inputName: "to",
                    source: RuntimeInputBindingSource.DurableValue,
                    durableValue: new RuntimeDurableValueReference("customerEmail"))
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string> { ["role"] = "leaf" });

        var root = new ExecutableNode(
            executableNodeId: "root",
            authoredActivityId: "authored-root",
            activityType: "Elsa.Sequence",
            activityTypeVersion: "1.0.0",
            descriptorType: "Elsa.Activities.SequenceDescriptor",
            descriptorPayload: Json("""{ "kind": "Send" }"""),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Body", [child])]);

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                ArtifactId: artifactId,
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: artifactVersion,
                ArtifactHash: $"hash-{artifactId}"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-1"] = new("resume-1", "node-child", "Bookmark", new Dictionary<string, string> { ["stimulus"] = "Http" })
            },
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string> { ["slice"] = "slice-1" });
    }

    private static WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        string artifactId,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: artifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: publishedAt ?? DateTimeOffset.UnixEpoch,
            PublishedAt: publishedAt,
            Scope: scope,
            ExpiresAt: expiresAt,
            Layout: [new WorkflowExecutableLayoutRecord("node-a", 1, 2, 3, 4, Json("""{ "k": "v" }"""))]);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}

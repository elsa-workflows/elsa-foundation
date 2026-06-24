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
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore);

        await store.SaveAsync(Executable("artifact-1"));
        await store.SaveAsync(Executable("artifact-2"));

        var found = await store.FindAsync("artifact-1");
        Assert.NotNull(found);
        Assert.Equal("artifact-1", found!.Identity.ArtifactId);
        Assert.Equal("definition-1", found.Identity.DefinitionId);
        Assert.Equal("hash-artifact-1", found.Identity.ArtifactHash);
        Assert.Equal("WorkflowDefinitionVersion", found.Identity.Source!.SourceKind);

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
    public async Task Save_Replaces_Existing_Executable(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore);

        await store.SaveAsync(Executable("artifact-1", artifactVersion: "1"));
        await store.SaveAsync(Executable("artifact-1", artifactVersion: "2"));

        var found = await store.FindAsync("artifact-1");
        Assert.Equal("2", found!.Identity.ArtifactVersion);
        Assert.Single(await store.ListAsync());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Find_Returns_Null_When_Absent(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore);

        Assert.Null(await store.FindAsync("missing"));
        Assert.Empty(await store.ListAsync());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task List_Excludes_Transient_Test_Run_Executables_And_Delete_Removes_Them(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore);

        await store.SaveAsync(Executable("artifact-1"));
        await store.SaveAsync(Executable("test-artifact-1", scope: WorkflowExecutableScope.TransientTestRun, expiresAt: DateTimeOffset.UtcNow.AddMinutes(30)));

        var all = await store.ListAsync();
        Assert.Equal("artifact-1", Assert.Single(all).Identity.ArtifactId);
        Assert.NotNull(await store.FindAsync("test-artifact-1"));

        Assert.True(await store.DeleteAsync("test-artifact-1"));
        Assert.Null(await store.FindAsync("test-artifact-1"));
    }

    [Fact]
    public void Serialization_Omits_Derived_Node_Projections()
    {
        var json = JsonSerializer.Serialize(Executable("artifact-1"), GroundworkRuntimeJson.Options);

        // RootActivity is the single source of truth; Nodes/NodesById are rebuilt on load (asserted in
        // RoundTrips_Across_Providers) and must not be persisted.
        Assert.Contains("\"rootActivity\"", json);
        Assert.DoesNotContain("\"nodesById\"", json);
        Assert.DoesNotContain("\"nodes\"", json);
    }

    private static WorkflowExecutable Executable(
        string artifactId,
        string artifactVersion = "1",
        WorkflowExecutableScope scope = WorkflowExecutableScope.Published,
        DateTimeOffset? expiresAt = null)
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
                ArtifactHash: $"hash-{artifactId}",
                Source: new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "version-1", artifactVersion)),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-1"] = new("resume-1", "node-child", "Bookmark", new Dictionary<string, string> { ["stimulus"] = "Http" })
            },
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string> { ["slice"] = "slice-1" },
            scope: scope,
            expiresAt: expiresAt);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}

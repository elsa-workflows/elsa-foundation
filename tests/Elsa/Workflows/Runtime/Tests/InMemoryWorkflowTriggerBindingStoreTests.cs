using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InMemoryWorkflowTriggerBindingStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_IsUpsertKeyedByBindingId()
    {
        // The binding id is now (artifact, node, stimulusHash); re-saving the same triple upserts, while a
        // different stimulus hash on the same node is a distinct binding (a node can carry several descriptors).
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusType: "Event", stimulusHash: "sha256:v1"));
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusType: "Signal", stimulusHash: "sha256:v1"));

        var bindings = await store.ListByArtifactAsync("artifact-1");

        var binding = Assert.Single(bindings);
        Assert.Equal("Signal", binding.StimulusType);
    }

    [Fact]
    public async Task Save_KeepsDistinctBindings_ForMultipleHashesOnOneNode()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusHash: "sha256:v1"));
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusHash: "sha256:v2"));

        var bindings = await store.ListByArtifactAsync("artifact-1");

        Assert.Equal(2, bindings.Count);
        Assert.Equal(
            ["sha256:v1", "sha256:v2"],
            bindings.Select(b => b.StimulusHash).OrderBy(h => h, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ListByStimulus_MatchesAcrossArtifacts_ButFiltersByType()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusType: "Event", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-2", "node-a", stimulusType: "Event", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-3", "node-a", stimulusType: "Signal", stimulusHash: "sha256:shared"));

        var matches = await store.ListByStimulusAsync("Event", "sha256:shared");

        Assert.Equal(
            ["artifact-1", "artifact-2"],
            matches.Select(binding => binding.ArtifactId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeleteByArtifact_RemovesOnlyThatArtifactsBindings_AndReturnsCount()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-1", "node-a"));
        await store.SaveAsync(Binding("artifact-1", "node-b"));
        await store.SaveAsync(Binding("artifact-2", "node-a"));

        var removed = await store.DeleteByArtifactAsync("artifact-1");

        Assert.Equal(2, removed);
        Assert.Empty(await store.ListByArtifactAsync("artifact-1"));
        Assert.Single(await store.ListByArtifactAsync("artifact-2"));
    }

    private WorkflowTriggerBinding Binding(
        string artifactId,
        string nodeId,
        string stimulusType = "Event",
        string stimulusHash = "sha256:event:hello") =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(artifactId, nodeId, stimulusHash),
            ArtifactId: artifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:artifact",
            ExecutableNodeId: nodeId,
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: _now);
}

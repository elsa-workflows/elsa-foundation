using Elsa.Workflows.Runtime.Core.Contracts;
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

        var matches = (await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:shared"))).Items;

        Assert.Equal(
            ["artifact-1", "artifact-2"],
            matches.Select(binding => binding.ArtifactId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ListByStimulus_PagesOnlyActiveMatchesInStableBindingOrder()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-c", "node", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-a", "node", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-b", "node", stimulusHash: "sha256:shared") with { IsActive = false });
        await store.SaveAsync(Binding("artifact-d", "node", stimulusHash: "sha256:shared"));

        var first = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:shared", limit: 2));
        var second = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery(
                "Event",
                "sha256:shared",
                limit: 2,
                continuationToken: first.NextContinuationToken));

        Assert.Equal(
            ["artifact-a", "artifact-c"],
            first.Items.Select(binding => binding.ArtifactId));
        Assert.Equal(["artifact-d"], second.Items.Select(binding => binding.ArtifactId));
        Assert.Equal(3, first.TotalCount);
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task ListByStimulus_RejectsAContinuationFromAnotherStimulus()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-a", "node", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-b", "node", stimulusHash: "sha256:shared"));
        var first = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:shared", limit: 1));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(
                "Event",
                "sha256:other",
                limit: 1,
                continuationToken: first.NextContinuationToken)));
    }

    [Fact]
    public async Task ListByStimulus_RejectsATamperedContinuationChecksum()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-a", "node", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-b", "node", stimulusHash: "sha256:shared"));
        var first = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:shared", limit: 1));
        var continuation = Assert.IsType<string>(first.NextContinuationToken);
        var tampered = continuation[..^1] + (continuation[^1] == 'A' ? 'B' : 'A');

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(
                "Event",
                "sha256:shared",
                limit: 1,
                continuationToken: tampered)));
    }

    [Fact]
    public async Task ListByStimulus_ResumesAfterTheBoundaryAcrossConcurrentChanges()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-b", "node", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-d", "node", stimulusHash: "sha256:shared"));
        var first = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:shared", limit: 1));

        await store.SaveAsync(Binding("artifact-a", "node", stimulusHash: "sha256:shared"));
        await store.SaveAsync(Binding("artifact-c", "node", stimulusHash: "sha256:shared"));
        var resumed = await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery(
                "Event",
                "sha256:shared",
                limit: 10,
                continuationToken: first.NextContinuationToken));

        Assert.Equal("artifact-b", Assert.Single(first.Items).ArtifactId);
        Assert.Equal(["artifact-c", "artifact-d"], resumed.Items.Select(binding => binding.ArtifactId));
        Assert.Equal(4, resumed.TotalCount);
        Assert.Null(resumed.NextContinuationToken);
    }

    [Fact]
    public void PageQuery_RejectsBlankAndOversizedContinuations()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowTriggerBindingPageQuery("Event", "shared", continuationToken: " "));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowTriggerBindingPageQuery(
                "Event",
                "shared",
                continuationToken: new string('x', WorkflowTriggerBindingPageQuery.MaximumContinuationTokenLength + 1)));
    }

    [Fact]
    public void Page_RejectsNonAdvancingEmptyAndOversizedProviderContinuations()
    {
        var query = new WorkflowTriggerBindingPageQuery(
            "Event",
            "sha256:shared",
            limit: 1,
            continuationToken: "current");
        var binding = Binding("artifact-a", "node", stimulusHash: "sha256:shared");

        Assert.Throws<ArgumentException>(() =>
            new WorkflowTriggerBindingPage(query, [binding], totalCount: 1, nextContinuationToken: "current"));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowTriggerBindingPage(query, [], totalCount: 1, nextContinuationToken: "next"));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowTriggerBindingPage(
                query,
                [binding],
                totalCount: 1,
                nextContinuationToken: new string(
                    'x',
                    WorkflowTriggerBindingPageQuery.MaximumContinuationTokenLength + 1)));
    }

    [Fact]
    public async Task ListAllByStimulus_TraversesEveryBoundedPage()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        for (var index = 0; index <= WorkflowTriggerBindingPageQuery.MaximumLimit; index++)
        {
            await store.SaveAsync(Binding(
                $"artifact-{index:D3}",
                "node",
                stimulusHash: "sha256:shared"));
        }

        var matches = await store.ListAllByStimulusAsync("Event", "sha256:shared");

        Assert.Equal(WorkflowTriggerBindingPageQuery.MaximumLimit + 1, matches.Count);
        Assert.Equal(matches.Count, matches.Select(binding => binding.TriggerBindingId).Distinct().Count());
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

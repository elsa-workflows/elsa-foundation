using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InMemoryWorkflowTriggerBindingStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildId_IsBoundedDeterministicAndTupleSensitive()
    {
        var first = WorkflowTriggerBinding.BuildId(
            new string('a', 1_000),
            new string('b', 1_000),
            new string('c', 1_000));
        var replay = WorkflowTriggerBinding.BuildId(
            new string('a', 1_000),
            new string('b', 1_000),
            new string('c', 1_000));
        var differentTuple = WorkflowTriggerBinding.BuildId("ab", "c", "value");
        var ambiguousWithoutFraming = WorkflowTriggerBinding.BuildId("a", "bc", "value");

        Assert.Equal(first, replay);
        Assert.InRange(first.Length, 1, WorkflowTriggerBinding.MaximumIdLength);
        Assert.NotEqual(differentTuple, ambiguousWithoutFraming);
    }

    [Fact]
    public async Task Save_RejectsAnIdThatExceedsThePortableBound()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var binding = Binding("artifact-1", "node-a") with
        {
            TriggerBindingId = new string('x', WorkflowTriggerBinding.MaximumIdLength + 1)
        };

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SaveAsync(binding));
    }

    [Fact]
    public async Task PreparePublication_RejectsAnIdThatExceedsThePortableBound()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var binding = Binding("artifact-1", "node-a") with
        {
            TriggerBindingId = new string('x', WorkflowTriggerBinding.MaximumIdLength + 1),
            ActivationId = "publication-1",
            SlotId = "default"
        };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.PrepareActivationAsync("publication-1", [binding]));
    }

    [Fact]
    public async Task Save_IsUpsertKeyedByBindingId()
    {
        // The binding id is now (artifact, node, stimulusHash); re-saving the same triple upserts, while a
        // different stimulus hash on the same node is a distinct binding (a node can carry several descriptors).
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusType: "Event", stimulusHash: "sha256:v1"));
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusType: "Signal", stimulusHash: "sha256:v1"));

        var bindings = await store.ListAllByArtifactAsync("artifact-1");

        var binding = Assert.Single(bindings);
        Assert.Equal("Signal", binding.StimulusType);
    }

    [Fact]
    public async Task Save_KeepsDistinctBindings_ForMultipleHashesOnOneNode()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusHash: "sha256:v1"));
        await store.SaveAsync(Binding("artifact-1", "node-a", stimulusHash: "sha256:v2"));

        var bindings = await store.ListAllByArtifactAsync("artifact-1");

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

        var boundary = Assert.Single(first.Items).TriggerBindingId;
        var expectedResumed = (await store.ListAllByArtifactAsync("artifact-a"))
            .Concat(await store.ListAllByArtifactAsync("artifact-b"))
            .Concat(await store.ListAllByArtifactAsync("artifact-c"))
            .Concat(await store.ListAllByArtifactAsync("artifact-d"))
            .Where(binding => StringComparer.Ordinal.Compare(binding.TriggerBindingId, boundary) > 0)
            .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
            .Select(binding => binding.ArtifactId);
        Assert.Equal(expectedResumed, resumed.Items.Select(binding => binding.ArtifactId));
        Assert.Equal(4, resumed.TotalCount);
        Assert.Null(resumed.NextContinuationToken);
    }

    [Fact]
    public async Task ListByStimulusType_PagesOnlyActiveMatchesInStableBindingOrder()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-c", "node", stimulusType: "Event"));
        await store.SaveAsync(Binding("artifact-a", "node", stimulusType: "Event"));
        await store.SaveAsync(Binding("artifact-b", "node", stimulusType: "Event") with { IsActive = false });
        await store.SaveAsync(Binding("artifact-d", "node", stimulusType: "Event"));
        await store.SaveAsync(Binding("artifact-signal", "node", stimulusType: "Signal"));

        var first = await store.ListByStimulusTypeAsync(
            new WorkflowTriggerBindingTypePageQuery("Event", limit: 2));
        var second = await store.ListByStimulusTypeAsync(
            new WorkflowTriggerBindingTypePageQuery(
                "Event",
                limit: 2,
                continuationToken: first.NextContinuationToken));

        var expected = (await store.ListAllByArtifactAsync("artifact-a"))
            .Concat(await store.ListAllByArtifactAsync("artifact-c"))
            .Concat(await store.ListAllByArtifactAsync("artifact-d"))
            .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
            .Select(binding => binding.ArtifactId)
            .ToArray();
        Assert.Equal(
            expected.Take(2),
            first.Items.Select(binding => binding.ArtifactId));
        Assert.Equal(expected.Skip(2), second.Items.Select(binding => binding.ArtifactId));
        Assert.Equal(3, first.TotalCount);
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task ListByStimulusType_RejectsAContinuationFromAnotherType()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-a", "node", stimulusType: "Event"));
        await store.SaveAsync(Binding("artifact-b", "node", stimulusType: "Event"));
        var first = await store.ListByStimulusTypeAsync(
            new WorkflowTriggerBindingTypePageQuery("Event", limit: 1));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ListByStimulusTypeAsync(
                new WorkflowTriggerBindingTypePageQuery(
                    "Signal",
                    limit: 1,
                    continuationToken: first.NextContinuationToken)));
    }

    [Fact]
    public async Task PublicationAndArtifactPages_AreOrderedBoundedAndQueryBound()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(Binding("artifact-c", "node", stimulusHash: "sha256:c") with { ActivationId = "publication-a", SlotId = "default" });
        await store.SaveAsync(Binding("artifact-a", "node", stimulusHash: "sha256:a") with { ActivationId = "publication-a", SlotId = "default" });
        await store.SaveAsync(Binding("artifact-b", "node", stimulusHash: "sha256:b") with { ActivationId = "publication-a", SlotId = "default" });

        var first = await store.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery("publication-a", limit: 2));
        var second = await store.ListByActivationAsync(
            new WorkflowTriggerBindingActivationPageQuery(
                "publication-a",
                limit: 2,
                continuationToken: first.NextContinuationToken));

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(
            first.Items.Concat(second.Items).Select(binding => binding.TriggerBindingId).Order(StringComparer.Ordinal),
            first.Items.Concat(second.Items).Select(binding => binding.TriggerBindingId));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ListByArtifactAsync(
            new WorkflowTriggerBindingArtifactPageQuery(
                "artifact-a",
                continuationToken: first.NextContinuationToken)));
    }

    [Fact]
    public async Task ListAllByPublicationAndArtifact_TraversesEveryBoundedPage()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        for (var index = 0; index <= WorkflowTriggerBindingPageQuery.MaximumLimit; index++)
        {
            await store.SaveAsync(Binding(
                "artifact-shared",
                $"node-{index:D3}",
                stimulusHash: $"sha256:{index:D3}") with
            {
                ActivationId = "publication-shared",
                SlotId = "default"
            });
        }

        var publication = await store.ListAllByActivationAsync("publication-shared");
        var artifact = await store.ListAllByArtifactAsync("artifact-shared");

        Assert.Equal(WorkflowTriggerBindingPageQuery.MaximumLimit + 1, publication.Count);
        Assert.Equal(publication.Select(binding => binding.TriggerBindingId).Order(StringComparer.Ordinal), publication.Select(binding => binding.TriggerBindingId));
        Assert.Equal(publication, artifact);
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
    public async Task ListAllByStimulusType_TraversesEveryBoundedPage()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        for (var index = 0; index <= WorkflowTriggerBindingTypePageQuery.MaximumLimit; index++)
        {
            await store.SaveAsync(Binding(
                $"artifact-{index:D3}",
                "node",
                stimulusType: "Event",
                stimulusHash: $"sha256:event:{index:D3}"));
        }
        await store.SaveAsync(Binding("ignored-artifact", "node", stimulusType: "Signal"));

        var matches = await store.ListAllByStimulusTypeAsync("Event");

        Assert.Equal(WorkflowTriggerBindingTypePageQuery.MaximumLimit + 1, matches.Count);
        Assert.All(matches, binding => Assert.Equal("Event", binding.StimulusType));
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
        Assert.Empty(await store.ListAllByArtifactAsync("artifact-1"));
        Assert.Single(await store.ListAllByArtifactAsync("artifact-2"));
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

using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Manifests;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowTriggerBindingStoreTests
{
    // The same contract runs against the real Groundwork SQLite provider and an in-memory document store.
    // Identical behavior proves the by-stimulus (cross-artifact) index and the by-artifact index are
    // provider-neutral, which is the whole point of the trigger index (start any workflow from a stimulus,
    // regardless of the persistence provider the host composed).
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SaveAndQuery_ByStimulusAndArtifact_RoundTrips(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider, RuntimeManifest());
        IWorkflowTriggerBindingStore store = new GroundworkWorkflowTriggerBindingStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);

        await store.SaveAsync(Binding("artifact-1", "node-a", "Event", "hash-order"));
        await store.SaveAsync(Binding("artifact-2", "node-b", "Event", "hash-order"));
        await store.SaveAsync(Binding("artifact-3", "node-c", "Event", "hash-other"));
        await store.SaveAsync(Binding("artifact-4", "node-d", "Signal", "hash-order"));

        // Cross-artifact by-stimulus lookup returns every published artifact waiting on the same stimulus.
        var byStimulus = await store.ListByStimulusAsync("Event", "hash-order");
        Assert.Equal(new[] { "artifact-1", "artifact-2" }, byStimulus.Select(b => b.ArtifactId).OrderBy(x => x));

        // The composite route narrows a shared hash by stimulus type before documents are returned.
        var bySharedHashOtherType = await store.ListByStimulusAsync("Signal", "hash-order");
        Assert.Equal("artifact-4", Assert.Single(bySharedHashOtherType).ArtifactId);

        // Per-artifact lookup is scoped to the one artifact.
        var byArtifact = await store.ListByArtifactAsync("artifact-1");
        var single = Assert.Single(byArtifact);
        Assert.Equal("node-a", single.ExecutableNodeId);
        Assert.Equal("order-scope", single.CorrelationScope);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task DeleteByArtifact_RemovesOnlyThatArtifact(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider, RuntimeManifest());
        IWorkflowTriggerBindingStore store = new GroundworkWorkflowTriggerBindingStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);

        await store.SaveAsync(Binding("artifact-1", "node-a", "Event", "hash-order"));
        await store.SaveAsync(Binding("artifact-1", "node-b", "Event", "hash-order2"));
        await store.SaveAsync(Binding("artifact-2", "node-c", "Event", "hash-order"));

        var deleted = await store.DeleteByArtifactAsync("artifact-1");

        Assert.Equal(2, deleted);
        Assert.Empty(await store.ListByArtifactAsync("artifact-1"));
        var remaining = await store.ListByStimulusAsync("Event", "hash-order");
        Assert.Equal("artifact-2", Assert.Single(remaining).ArtifactId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListByStimulusType_ReturnsEveryBindingOfType_AcrossArtifacts(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider, RuntimeManifest());
        IWorkflowTriggerBindingStore store = new GroundworkWorkflowTriggerBindingStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);

        // Two HTTP bindings on different artifacts + hashes, plus one of another type.
        await store.SaveAsync(Binding("artifact-1", "node-a", "HttpEndpoint", "hash-1"));
        await store.SaveAsync(Binding("artifact-2", "node-b", "HttpEndpoint", "hash-2"));
        await store.SaveAsync(Binding("artifact-3", "node-c", "Event", "hash-3"));

        // A type-scoped full scan (no hash) returns both HTTP bindings and narrows out the Event one.
        var http = await store.ListByStimulusTypeAsync("HttpEndpoint");
        Assert.Equal(new[] { "artifact-1", "artifact-2" }, http.Select(b => b.ArtifactId).OrderBy(x => x));

        Assert.Empty(await store.ListByStimulusTypeAsync("Signal"));
    }

    private static WorkflowTriggerBinding Binding(string artifactId, string nodeId, string stimulusType, string stimulusHash) =>
        new(
            WorkflowTriggerBinding.BuildId(artifactId, nodeId, stimulusHash),
            artifactId,
            $"definition-{artifactId}",
            "1.0.0",
            $"hash-{artifactId}",
            nodeId,
            stimulusType,
            stimulusHash,
            "order-scope",
            new Dictionary<string, string> { ["slice"] = "w7" },
            new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero));

    private static StorageManifest RuntimeManifest() =>
        new RuntimeGroundworkStorageManifestSource().CreateDeclarationAsync().GetAwaiter().GetResult().Manifest;
}

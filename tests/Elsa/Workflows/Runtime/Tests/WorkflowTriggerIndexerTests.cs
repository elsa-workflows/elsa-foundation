using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowTriggerIndexerTests
{
    [Fact]
    public async Task Index_WritesExtractedBindings()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]),
            store);

        var bindings = await indexer.IndexAsync(Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event")));

        Assert.Single(bindings);
        var stored = Assert.Single(await store.ListByArtifactAsync("artifact-1"));
        Assert.Equal("Event", stored.StimulusType);
    }

    [Fact]
    public async Task Index_ReplacesPriorBindingsForSameArtifact_OnRepublish()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:new")]),
            store);
        // Seed a stale binding on the same artifact that the current publish no longer contains.
        await store.SaveAsync(StaleBinding("artifact-1", "node-old"));

        await indexer.IndexAsync(Executable("artifact-1", "sha256:v2", TriggerNode("node-event", "Elsa.Event")));

        var bindings = await store.ListByArtifactAsync("artifact-1");
        var binding = Assert.Single(bindings);
        Assert.Equal("node-event", binding.ExecutableNodeId);
    }

    [Fact]
    public async Task Index_LeavesOtherArtifactsBindingsIntact()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(StaleBinding("artifact-2", "node-x"));
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:v1")]),
            store);

        await indexer.IndexAsync(Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event")));

        Assert.Single(await store.ListByArtifactAsync("artifact-2"));
    }

    [Fact]
    public async Task Index_NotifiesObservers_AfterSave_WithNewBindings()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var observer = new RecordingObserver();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]),
            store,
            [observer]);

        await indexer.IndexAsync(Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event")));

        var snapshot = Assert.Single(observer.Snapshots);
        Assert.Equal("artifact-1", snapshot.ArtifactId);
        var binding = Assert.Single(snapshot.Bindings);
        Assert.Equal("node-event", binding.ExecutableNodeId);
        // Observer runs after the write: the binding it was handed is already durable in the store.
        Assert.Single(await store.ListByArtifactAsync("artifact-1"));
    }

    [Fact]
    public async Task Index_ObserverFailure_PropagatesAndFailsPublish()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:v1")]),
            store,
            [new ThrowingObserver()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            indexer.IndexAsync(Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event"))).AsTask());
    }

    [Fact]
    public async Task Index_RunsValidators_BeforeAnyWrite_WithTheExtractedBindings()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var validator = new RecordingValidator(store);
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]),
            store,
            validators: [validator]);

        await indexer.IndexAsync(Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event")));

        var snapshot = Assert.Single(validator.Snapshots);
        Assert.Equal("artifact-1", snapshot.ArtifactId);
        Assert.Single(snapshot.Bindings);
        // Pre-write proof: when the validator ran, the store held nothing for the artifact yet.
        Assert.Empty(validator.BindingsInStoreAtValidation);
    }

    [Fact]
    public async Task Index_ValidatorFailure_FailsThePublish_WithTheStoreUntouched()
    {
        // The seam's load-bearing property (issue #592 item 2): a validator throw fails the publish BEFORE the
        // delete/save, so the artifact's prior bindings survive intact — no poisoned index, no rollback needed.
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(StaleBinding("artifact-1", "node-old"));
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:new")]),
            store,
            validators: [new ThrowingValidator()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            indexer.IndexAsync(Executable("artifact-1", "sha256:v2", TriggerNode("node-event", "Elsa.Event"))).AsTask());

        // The prior generation is still durable, not deleted; the new binding was never written.
        var binding = Assert.Single(await store.ListByArtifactAsync("artifact-1"));
        Assert.Equal("node-old", binding.ExecutableNodeId);
    }

    private static WorkflowExecutable Executable(string artifactId, string hash, ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", hash),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            publishedAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());

    private static WorkflowTriggerBinding StaleBinding(string artifactId, string nodeId) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(artifactId, nodeId, "sha256:old"),
            ArtifactId: artifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:old",
            ExecutableNodeId: nodeId,
            StimulusType: "Event",
            StimulusHash: "sha256:old",
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UnixEpoch);

    private static ExecutableNode TriggerNode(string nodeId, string activityType)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType });
    }

    private sealed class FakeProvider(string activityType, string stimulusType, string stimulusHash) : IActivityTriggerStimulusProvider
    {
        public IReadOnlyCollection<TriggerStimulusDescriptor> Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? [new TriggerStimulusDescriptor(stimulusType, stimulusHash)]
                : [];
    }

    private sealed class RecordingObserver : IWorkflowTriggerIndexObserver
    {
        public List<WorkflowTriggerIndexSnapshot> Snapshots { get; } = new();

        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : IWorkflowTriggerIndexObserver
    {
        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("observer boom");
    }

    /// <summary>Records the snapshot it was handed plus what the store held for the artifact at validation time.</summary>
    private sealed class RecordingValidator(IWorkflowTriggerBindingStore store) : IWorkflowTriggerIndexValidator
    {
        public List<WorkflowTriggerIndexSnapshot> Snapshots { get; } = new();
        public IReadOnlyCollection<WorkflowTriggerBinding> BindingsInStoreAtValidation { get; private set; } = [];

        public async ValueTask ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            BindingsInStoreAtValidation = await store.ListByArtifactAsync(snapshot.ArtifactId, cancellationToken);
        }
    }

    private sealed class ThrowingValidator : IWorkflowTriggerIndexValidator
    {
        public ValueTask ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("validator boom");
    }
}

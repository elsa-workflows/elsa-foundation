using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowTriggerIndexerTests
{
    [Fact]
    public async Task PublicationScopedBindingsPreserveIndependentAuthoritiesThatShareAnArtifact()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:shared")]),
            store);
        var executable = Executable("artifact-shared", "sha256:v1", TriggerNode("node-event", "Elsa.Event"));

        await indexer.PrepareActivationAsync(executable, "publication-default-v1", "slot-default");
        await indexer.PrepareActivationAsync(executable, "publication-blue", "slot-blue");

        Assert.Empty((await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:event:shared"))).Items);
        var defaultBinding = Assert.Single(await store.ListAllByActivationAsync("publication-default-v1"));
        var blueBinding = Assert.Single(await store.ListAllByActivationAsync("publication-blue"));
        Assert.Equal("publication-default-v1", defaultBinding.ActivationId);
        Assert.Equal("slot-default", defaultBinding.SlotId);
        Assert.Equal("publication-blue", blueBinding.ActivationId);
        Assert.Equal("slot-blue", blueBinding.SlotId);
        Assert.NotEqual(defaultBinding.TriggerBindingId, blueBinding.TriggerBindingId);

        await store.ActivateAsync("publication-default-v1", replacedActivationId: null);
        await store.ActivateAsync("publication-blue", replacedActivationId: null);

        Assert.Equal(
            ["publication-blue", "publication-default-v1"],
            (await store.ListByStimulusAsync(
                    new WorkflowTriggerBindingPageQuery("Event", "sha256:event:shared"))).Items
                .Select(binding => binding.ActivationId)
                .Order(StringComparer.Ordinal));

        await indexer.PrepareActivationAsync(executable, "publication-default-v2", "slot-default");
        await store.ActivateAsync("publication-default-v2", replacedActivationId: "publication-default-v1");

        Assert.Equal(
            ["publication-blue", "publication-default-v2"],
            (await store.ListByStimulusAsync(
                    new WorkflowTriggerBindingPageQuery("Event", "sha256:event:shared"))).Items
                .Select(binding => binding.ActivationId)
                .Order(StringComparer.Ordinal));

        await store.DeleteByActivationAsync("publication-default-v1");
        await store.DeleteByActivationAsync("publication-default-v2");

        var survivingBinding = Assert.Single((await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:event:shared"))).Items);
        Assert.Equal("publication-blue", survivingBinding.ActivationId);
        Assert.Equal("slot-blue", survivingBinding.SlotId);
    }

    [Fact]
    public async Task PrepareActivation_WritesExtractedBindings_AsPreparedRows()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]),
            store);

        var bindings = await indexer.PrepareActivationAsync(
            Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event")),
            "activation-1",
            "slot-default");

        Assert.Single(bindings);
        var stored = Assert.Single(await store.ListAllByArtifactAsync("artifact-1"));
        Assert.Equal("Event", stored.StimulusType);
        Assert.Equal("activation-1", stored.ActivationId);
        Assert.Equal("slot-default", stored.SlotId);
        // Prepared, not serving: only the coordinator's activate step exposes it to the router.
        Assert.False(stored.IsActive);
        Assert.Empty((await store.ListByStimulusAsync(
            new WorkflowTriggerBindingPageQuery("Event", "sha256:event:hello"))).Items);
    }

    [Fact]
    public async Task PrepareActivation_LeavesEveryOtherActivationOnTheSameArtifactIntact()
    {
        // The removed IndexAsync write path opened with DeleteByArtifactAsync, so preparing one activation wiped
        // every other activation's projection for a shared artifact — a wipe no activation lifecycle authorized
        // (FR-B-006 writer census, finding 1). Preparation is activation-scoped and touches nothing else.
        var store = new InMemoryWorkflowTriggerBindingStore();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:new")]),
            store);
        await store.SaveAsync(StaleBinding("artifact-1", "node-old"));

        await indexer.PrepareActivationAsync(
            Executable("artifact-1", "sha256:v2", TriggerNode("node-event", "Elsa.Event")),
            "activation-2",
            "slot-blue");

        var bindings = await store.ListAllByArtifactAsync("artifact-1");
        Assert.Equal(["node-event", "node-old"], bindings.Select(x => x.ExecutableNodeId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PrepareActivation_LeavesOtherArtifactsBindingsIntact()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(StaleBinding("artifact-2", "node-x"));
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:v1")]),
            store);

        await indexer.PrepareActivationAsync(
            Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event")),
            "activation-1",
            "slot-default");

        Assert.Single(await store.ListAllByArtifactAsync("artifact-2"));
    }

    [Fact]
    public async Task PrepareActivation_RunsValidators_BeforeAnyWrite_WithTheExtractedBindings()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        var validator = new RecordingValidator(store);
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event:hello")]),
            store,
            validators: [validator]);

        await indexer.PrepareActivationAsync(
            Executable("artifact-1", "sha256:v1", TriggerNode("node-event", "Elsa.Event")),
            "activation-1",
            "slot-default");

        var snapshot = Assert.Single(validator.Snapshots);
        Assert.Equal("artifact-1", snapshot.ArtifactId);
        Assert.Single(snapshot.Bindings);
        // Pre-write proof: when the validator ran, the store held nothing for the artifact yet.
        Assert.Empty(validator.BindingsInStoreAtValidation);
    }

    [Fact]
    public async Task PrepareActivation_ValidatorFailure_FailsTheActivation_WithTheStoreUntouched()
    {
        // The seam's load-bearing property (issue #592 item 2): a validator throw fails the activation BEFORE the
        // projection is written, so the artifact's prior bindings survive intact — no poisoned index.
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(StaleBinding("artifact-1", "node-old"));
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:new")]),
            store,
            validators: [new ThrowingValidator()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            indexer.PrepareActivationAsync(
                Executable("artifact-1", "sha256:v2", TriggerNode("node-event", "Elsa.Event")),
                "activation-2",
                "slot-default").AsTask());

        // The prior generation is still durable, not deleted; the new binding was never written.
        var binding = Assert.Single(await store.ListAllByArtifactAsync("artifact-1"));
        Assert.Equal("node-old", binding.ExecutableNodeId);
    }

    [Fact]
    public async Task PrepareActivation_InvalidLaterNode_LeavesEverySeededBindingUntouched()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(StaleBinding("artifact-1", "node-old-1"));
        await store.SaveAsync(StaleBinding("artifact-1", "node-old-2"));
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:new")]),
            store);
        var executable = Executable(
            "artifact-1",
            "sha256:v2",
            ActionNode(
                "root",
                "Elsa.Sequence",
                TriggerNode("node-invalid", "Elsa.Unknown"),
                TriggerNode("node-valid", "Elsa.Event")));

        var exception = await Assert.ThrowsAsync<WorkflowTriggerPreflightException>(() =>
            indexer.PrepareActivationAsync(executable, "activation-2", "slot-default").AsTask());

        Assert.Equal("node-invalid", exception.ExecutableNodeId);
        var bindings = await store.ListAllByArtifactAsync("artifact-1");
        Assert.Equal(["node-old-1", "node-old-2"], bindings.Select(x => x.ExecutableNodeId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PrepareActivation_UsesCompletedPreflightBindingSet_AndValidatorFailurePreservesSeededBindings()
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        await store.SaveAsync(StaleBinding("artifact-1", "node-old-1"));
        await store.SaveAsync(StaleBinding("artifact-1", "node-old-2"));
        var outcome = PreflightOutcome(
            Binding("node-new-1", "sha256:new:1"),
            Binding("node-new-2", "sha256:new:2"));
        var extractor = new PreflightOnlyExtractor(outcome);
        var validator = new RecordingThrowingValidator();
        var indexer = new WorkflowTriggerIndexer(extractor, store, validators: [validator]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            indexer.PrepareActivationAsync(
                Executable("artifact-1", "sha256:v2", TriggerNode("node-new-1", "Elsa.Event")),
                "activation-2",
                "slot-default").AsTask());

        Assert.Equal("validator boom", exception.Message);
        var validated = Assert.Single(validator.Snapshots);
        Assert.Equal(["node-new-1", "node-new-2"], validated.Bindings.Select(x => x.ExecutableNodeId).Order(StringComparer.Ordinal));
        var stored = await store.ListAllByArtifactAsync("artifact-1");
        Assert.Equal(["node-old-1", "node-old-2"], stored.Select(x => x.ExecutableNodeId).Order(StringComparer.Ordinal));
        Assert.Equal(1, extractor.EvaluateCallCount);
        Assert.Equal(0, extractor.ExtractCallCount);
    }

    private static WorkflowExecutable Executable(string artifactId, string hash, ExecutableNode root) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", hash),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

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

    private static WorkflowTriggerBinding Binding(string nodeId, string stimulusHash) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId("artifact-1", nodeId, stimulusHash),
            ArtifactId: "artifact-1",
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:v2",
            ExecutableNodeId: nodeId,
            StimulusType: "Event",
            StimulusHash: stimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UnixEpoch);

    private static WorkflowTriggerPreflightOutcome PreflightOutcome(params WorkflowTriggerBinding[] bindings)
    {
        var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:v2");
        var nodeOutcomes = bindings
            .GroupBy(x => x.ExecutableNodeId, StringComparer.Ordinal)
            .Select(x => new WorkflowTriggerNodePreflightOutcome(
                x.Key,
                "Elsa.Event",
                "provider.event",
                WorkflowTriggerPreflightStatus.Registered,
                x.ToArray()))
            .ToArray();
        return new WorkflowTriggerPreflightOutcome(identity, nodeOutcomes);
    }

    private static ExecutableNode TriggerNode(string nodeId, string activityType)
        => Node(nodeId, activityType, TriggerNodeMetadata.TriggerExecutionType);

    private static ExecutableNode ActionNode(string nodeId, string activityType, params ExecutableNode[] children)
        => Node(nodeId, activityType, "Action", children);

    private static ExecutableNode Node(string nodeId, string activityType, string executionType, params ExecutableNode[] children)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var childSlots = children.Length == 0
            ? Array.Empty<ExecutableChildSlot>()
            : [new ExecutableChildSlot("Body", children)];
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = executionType },
            childSlots: childSlots);
    }

    private sealed class FakeProvider(string activityType, string stimulusType, string stimulusHash) : IActivityTriggerStimulusProvider
    {
        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor(stimulusType, stimulusHash)])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    /// <summary>Records the snapshot it was handed plus what the store held for the artifact at validation time.</summary>
    private sealed class RecordingValidator(IWorkflowTriggerBindingStore store) : IWorkflowTriggerIndexValidator
    {
        public List<WorkflowTriggerIndexSnapshot> Snapshots { get; } = new();
        public IReadOnlyCollection<WorkflowTriggerBinding> BindingsInStoreAtValidation { get; private set; } = [];

        public async ValueTask ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            BindingsInStoreAtValidation = await store.ListAllByArtifactAsync(snapshot.ArtifactId, cancellationToken);
        }
    }

    private sealed class ThrowingValidator : IWorkflowTriggerIndexValidator
    {
        public ValueTask ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("validator boom");
    }

    private sealed class RecordingThrowingValidator : IWorkflowTriggerIndexValidator
    {
        public List<WorkflowTriggerIndexSnapshot> Snapshots { get; } = new();

        public ValueTask ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            throw new InvalidOperationException("validator boom");
        }
    }

    private sealed class PreflightOnlyExtractor(WorkflowTriggerPreflightOutcome outcome) : IWorkflowTriggerBindingExtractor
    {
        public int EvaluateCallCount { get; private set; }
        public int ExtractCallCount { get; private set; }

        public WorkflowTriggerPreflightOutcome Evaluate(WorkflowExecutable executable)
        {
            EvaluateCallCount++;
            return outcome;
        }

        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable)
        {
            ExtractCallCount++;
            throw new InvalidOperationException("The indexer used the legacy extraction path instead of the completed preflight outcome.");
        }
    }
}

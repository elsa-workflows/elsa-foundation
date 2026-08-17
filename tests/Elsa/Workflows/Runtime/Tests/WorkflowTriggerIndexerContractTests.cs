using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The <see cref="IWorkflowTriggerIndexer"/> extension point after the FR-B-006 writer census closed its most
/// dangerous back door (spec 151, T041/T045).
/// </summary>
/// <remarks>
/// The removed shape was <c>IndexAsync(executable, ct)</c> plus the default-interface fallback
/// <c>PrepareActivationAsync =&gt; IndexAsync</c>. It had no live production caller, but it was the signature the
/// extension-point catalog documented, so an implementer who provided only it was silently routed — through the
/// fallback — into an artifact-wide delete-and-rewrite that bypassed prepare/activate entirely and wiped the
/// projections of every other activation sharing the artifact. These tests pin the two properties that make that
/// impossible: the legacy shape no longer satisfies the contract at all, and the shipped implementations no
/// longer contain the artifact-scoped write path it drove.
/// </remarks>
public sealed class WorkflowTriggerIndexerContractTests
{
    [Fact]
    public void The_contract_exposes_only_the_activation_scoped_signature()
    {
        var methods = typeof(IWorkflowTriggerIndexer).GetMethods();

        var method = Assert.Single(methods);
        Assert.Equal(nameof(IWorkflowTriggerIndexer.PrepareActivationAsync), method.Name);
        Assert.Equal(
            ["executable", "activationId", "slotId", "cancellationToken"],
            method.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void The_activation_scoped_signature_has_no_default_implementation_so_a_partial_implementer_cannot_compile()
    {
        // This is the "fails loudly" guarantee in its only observable form: an interface method carrying a default
        // body is NOT abstract, and that default body is exactly what used to swallow a partial implementation.
        // With the method abstract, a type providing only the legacy signature fails at compile time.
        var method = typeof(IWorkflowTriggerIndexer).GetMethod(nameof(IWorkflowTriggerIndexer.PrepareActivationAsync))!;

        Assert.True(method.IsAbstract);
        Assert.DoesNotContain(
            typeof(IWorkflowTriggerIndexer).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
            candidate => !candidate.IsAbstract);
    }

    [Fact]
    public void An_indexer_written_to_the_legacy_signature_alone_does_not_satisfy_the_contract()
    {
        // LegacyOnlyIndexer is the exact shape the old catalog documented. It still compiles as a standalone
        // class and is simply not an IWorkflowTriggerIndexer any more, so it can never be registered as one.
        Assert.False(typeof(IWorkflowTriggerIndexer).IsAssignableFrom(typeof(LegacyOnlyIndexer)));
    }

    [Fact]
    public void The_default_implementation_no_longer_carries_an_artifact_scoped_write_path()
    {
        // The recurring decorator's half of this assertion lives in Elsa.Workflows.Runtime.Scheduling.Tests,
        // which is where that type is referenced from.
        Assert.Null(typeof(WorkflowTriggerIndexer).GetMethod("IndexAsync"));
    }

    [Fact]
    public async Task Preparation_never_deletes_by_artifact()
    {
        // The behavioural half: the wipe is gone from the code path, not merely from the public surface.
        var store = new ArtifactDeleteCountingStore();
        var indexer = new WorkflowTriggerIndexer(
            new WorkflowTriggerBindingExtractor([new FakeProvider("Elsa.Event", "Event", "sha256:event")]),
            store);

        await indexer.PrepareActivationAsync(Executable("artifact-1"), "activation-1", "slot-default");

        Assert.Equal(0, store.DeleteByArtifactCalls);
        var prepared = Assert.Single(store.Prepared["activation-1"]);
        Assert.False(prepared.IsActive);
        Assert.Equal("activation-1", prepared.ActivationId);
    }

    /// <summary>The pre-census extension-point shape. Kept as a type to prove it no longer satisfies the contract.</summary>
    private sealed class LegacyOnlyIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default) =>
            new(Array.Empty<WorkflowTriggerBinding>());
    }

    /// <summary>Counts any reach for the artifact-wide wipe and records what preparation actually wrote.</summary>
    private sealed class ArtifactDeleteCountingStore : IWorkflowTriggerBindingStore
    {
        private readonly InMemoryWorkflowTriggerBindingStore _inner = new();

        public int DeleteByArtifactCalls { get; private set; }

        public Dictionary<string, IReadOnlyCollection<WorkflowTriggerBinding>> Prepared { get; } = new(StringComparer.Ordinal);

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(binding, cancellationToken);

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default)
        {
            Prepared[activationId] = bindings;
            return _inner.PrepareActivationAsync(activationId, bindings, cancellationToken);
        }

        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByActivationAsync(query, cancellationToken);

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default) =>
            _inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);

        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
            _inner.DeleteByActivationAsync(activationId, cancellationToken);

        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            DeleteByArtifactCalls++;
            return _inner.DeleteByArtifactAsync(artifactId, cancellationToken);
        }

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByStimulusAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByArtifactAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListByStimulusTypeAsync(query, cancellationToken);
    }

    private sealed class FakeProvider(string activityType, string stimulusType, string stimulusHash) : IActivityTriggerStimulusProvider
    {
        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, activityType)
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor(stimulusType, stimulusHash)])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    private static WorkflowExecutable Executable(string artifactId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var root = new ExecutableNode(
            executableNodeId: "node-event",
            authoredActivityId: "authored-node-event",
            activityType: "Elsa.Event",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType },
            childSlots: []);

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }
}

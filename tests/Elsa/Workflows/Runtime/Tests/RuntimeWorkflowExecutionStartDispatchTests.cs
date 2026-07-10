using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeWorkflowExecutionStartDispatchTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DispatchAsync_SendsStartCommandThroughWorkflowExecutionActor()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var executable = NewExecutable();
        await store.SaveAsync(executable);
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        var result = await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var activation = Assert.Single(agentProvider.ActivationRequests);
        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-1", result.WorkflowExecutionId);
        Assert.Equal("wfexec-1", activation.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionActorActivationReason.Start, activation.Reason);
        Assert.Equal("runtime-test", activation.RequestedBy);
        Assert.Equal("wfexec-1", envelope.WorkflowExecutionId);
        Assert.Equal("command-1", envelope.Command.CommandId);
        Assert.Equal("envelope-1", envelope.EnvelopeId);
        Assert.Equal("wfexec-1:start:artifact-1", envelope.IdempotencyKey);
        Assert.Equal(WorkflowExecutionCommandKind.Start, envelope.Command.Kind);
        Assert.Equal(_now, envelope.EnqueuedAt);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.Equal(agentProvider.Agent.Descriptor.WorkflowExecutionId, result.Agent.WorkflowExecutionId);
        Assert.Equal(agentProvider.Agent.Descriptor.AgentId, result.Agent.AgentId);
    }

    [Fact]
    public async Task DispatchAsync_PinsExecutableIdentityInStartPayload()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal("artifact-1", payload.RequestedArtifactId);
        Assert.Equal("artifact-1", payload.PinnedExecutable.ArtifactId);
        Assert.Equal("definition-1", payload.PinnedExecutable.DefinitionId);
        Assert.Equal("version-1", payload.PinnedExecutable.DefinitionVersionId);
        Assert.Equal("1.0.0", payload.PinnedExecutable.ArtifactVersion);
        Assert.Equal("sha256:test", payload.PinnedExecutable.ArtifactHash);
    }

    [Fact]
    public async Task DispatchAsync_RejectsUnknownArtifactBeforeAgentActivation()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() => dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("missing-artifact", "runtime-test")).AsTask());

        Assert.Contains("missing-artifact", exception.Message);
        Assert.Equal("missing-artifact", exception.ArtifactId);
        Assert.Empty(agentProvider.ActivationRequests);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_DispatchesArtifactWithNoReferencesUnchanged()
    {
        // Backward-compat seam (ADR 0040): an artifact saved directly into the single store with no source reference —
        // the direct/seeded runtime path — is dispatched unchanged. The reference gate engages only once references exist.
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        var result = await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.Single(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_DispatchesArtifactWithLivePublishedReference()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var referenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        await referenceStore.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published));
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider, referenceStore);

        var result = await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.Single(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_RejectsPublishedDispatchOfArtifactWithOnlyTestRunReference()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var referenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        await referenceStore.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(30)));
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider, referenceStore);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test")).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, exception.Reason);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_RejectsWithExpiryReasonWhenOnlyMatchingReferenceExpired()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var referenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        await referenceStore.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(-1)));
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider, referenceStore);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "test-run"), WorkflowExecutableReferenceScope.TestRun).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.Expired, exception.Reason);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_CarriesSeededVariablesAndInputsIntoStartPayload()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            artifactId: "artifact-1",
            requestedBy: "test",
            variables: new Dictionary<string, object?> { ["greeting"] = "Hello" },
            inputs: new Dictionary<string, object?> { ["name"] = "World" }));

        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal("Hello", payload.Variables["greeting"].GetString());
        Assert.Equal("World", payload.Inputs["name"].GetString());
    }

    [Fact]
    public async Task DispatchAsync_UsesProvidedWorkflowExecutionIdAndIdempotencyKey()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            artifactId: "artifact-1",
            requestedBy: "test",
            workflowExecutionId: "wfexec-provided",
            idempotencyKey: "caller-key",
            metadata: new Dictionary<string, string> { ["caller"] = "unit-test" }));

        var activation = Assert.Single(agentProvider.ActivationRequests);
        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-provided", activation.WorkflowExecutionId);
        Assert.Equal("test", activation.RequestedBy);
        Assert.Equal("caller-key", envelope.IdempotencyKey);
        Assert.Equal("unit-test", envelope.Command.Metadata["caller"]);
        Assert.Equal("artifact-1", envelope.Command.Metadata["runtime.artifactId"]);
    }

    [Fact]
    public async Task DispatchAsync_ForwardsDispatchOptionsToAgentEnqueue()
    {
        // Spec 089 E-D4 (FR-019): the dispatcher forwards the caller's dispatch options verbatim into the agent
        // mailbox so an in-process inline drain builds activity execution contexts from the ambient request scope.
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);
        var options = new WorkflowExecutionCommandDispatchOptions();

        await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"), dispatchOptions: options);

        Assert.Same(options, Assert.Single(agentProvider.Agent.DispatchOptions));
    }

    [Fact]
    public async Task DispatchAsync_WithoutDispatchOptions_EnqueuesDefaultOptions()
    {
        // Absent options ⇒ the dispatcher enqueues WorkflowExecutionCommandDispatchOptions.Default, pinning the
        // pre-089 single-arg behavior (no ambient services attached).
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test"));

        var options = Assert.Single(agentProvider.Agent.DispatchOptions);
        Assert.Same(WorkflowExecutionCommandDispatchOptions.Default, options);
        Assert.Null(options!.AmbientServices);
    }

    private WorkflowStartDispatcher NewDispatcher(
        InMemoryWorkflowExecutableStore store,
        RecordingAgentProvider agentProvider,
        InMemoryWorkflowExecutableSourceReferenceStore? referenceStore = null) =>
        new(
            store,
            referenceStore ?? new InMemoryWorkflowExecutableSourceReferenceStore(),
            agentProvider,
            new IncrementingRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(_now));

    private WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        string artifactId,
        WorkflowExecutableReferenceScope scope,
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
            CreatedAt: _now,
            PublishedAt: scope == WorkflowExecutableReferenceScope.Published ? _now : null,
            Scope: scope,
            ExpiresAt: expiresAt);

    private static WorkflowExecutable NewExecutable() =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: NewNode("node-root"),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private sealed class RecordingAgentProvider : IWorkflowExecutionActorProvider
    {
        public RecordingAgent Agent { get; } = new("wfexec-unassigned");
        public List<WorkflowExecutionActorActivationRequest> ActivationRequests { get; } = [];
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        {
            ActivationRequests.Add(request);
            Agent.AssignWorkflowExecutionId(request.WorkflowExecutionId);
            return ValueTask.FromResult<IWorkflowExecutionActor>(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAgent(string workflowExecutionId) : IWorkflowExecutionActor
    {
        private string _workflowExecutionId = workflowExecutionId;

        public WorkflowExecutionActorDescriptor Descriptor { get; private set; } = NewDescriptor(workflowExecutionId);
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public List<WorkflowExecutionCommandDispatchOptions?> DispatchOptions { get; } = [];

        public void AssignWorkflowExecutionId(string workflowExecutionId)
        {
            _workflowExecutionId = workflowExecutionId;
            Descriptor = NewDescriptor(workflowExecutionId);
        }

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            Record(envelope, null);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken = default) =>
            Record(envelope, options);

        private ValueTask<WorkflowExecutionCommandDispatchResult> Record(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions? options)
        {
            Envelopes.Add(envelope);
            DispatchOptions.Add(options);
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: _workflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Accepted,
                recordedAt: DateTimeOffset.UtcNow));
        }

        private static WorkflowExecutionActorDescriptor NewDescriptor(string workflowExecutionId) =>
            new(
                workflowExecutionId: workflowExecutionId,
                agentId: $"recording:{workflowExecutionId}",
                providerName: "Recording",
                status: WorkflowExecutionActorStatus.Active,
                capabilities: WorkflowExecutionActorCapabilities.None,
                activatedAt: DateTimeOffset.UtcNow);
    }

    private sealed class IncrementingRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        private int _workflowExecutionIndex;
        private int _commandIndex;
        private int _envelopeIndex;

        public string NewWorkflowExecutionId() => $"wfexec-{++_workflowExecutionIndex}";

        public string NewWorkflowExecutionCommandId() => $"command-{++_commandIndex}";

        public string NewWorkflowExecutionCommandEnvelopeId() => $"envelope-{++_envelopeIndex}";

        public string NewActivityExecutionId() => throw new NotSupportedException("Start dispatch does not schedule activities.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

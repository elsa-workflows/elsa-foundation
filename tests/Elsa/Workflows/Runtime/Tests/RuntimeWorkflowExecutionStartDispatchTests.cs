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
    public async Task DispatchAsync_SendsStartCommandThroughWorkflowExecutionAgent()
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
        Assert.Equal(WorkflowExecutionAgentActivationReason.Start, activation.Reason);
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
    public async Task DispatchAsync_RejectsTransientTestRunArtifactBeforeAgentActivation()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable(scope: WorkflowExecutableScope.TransientTestRun, expiresAt: _now.AddMinutes(30)));
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() => dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "runtime-test")).AsTask());

        Assert.Empty(agentProvider.ActivationRequests);
        Assert.Empty(agentProvider.Agent.Envelopes);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task DispatchTransientAsync_AllowsTransientTestRunArtifact()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var executable = NewExecutable(scope: WorkflowExecutableScope.TransientTestRun, expiresAt: _now.AddMinutes(30));
        var agentProvider = new RecordingAgentProvider();
        var dispatcher = NewDispatcher(store, agentProvider);

        var result = await dispatcher.DispatchTransientAsync(new WorkflowExecutionStartDispatchRequest("artifact-1", "designer-test"), executable);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.Single(agentProvider.ActivationRequests);
        Assert.Single(agentProvider.Agent.Envelopes);
        Assert.Empty(await store.ListAsync());
        Assert.NotNull(await store.FindAsync("artifact-1"));
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

    private WorkflowExecutionStartDispatcher NewDispatcher(
        InMemoryWorkflowExecutableStore store,
        RecordingAgentProvider agentProvider) =>
        new(
            store,
            agentProvider,
            new IncrementingRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(_now));

    private static WorkflowExecutable NewExecutable(
        WorkflowExecutableScope scope = WorkflowExecutableScope.Published,
        DateTimeOffset? expiresAt = null) =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: NewNode("node-root"),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            scope: scope,
            expiresAt: expiresAt);

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

    private sealed class RecordingAgentProvider : IWorkflowExecutionAgentProvider
    {
        public RecordingAgent Agent { get; } = new("wfexec-unassigned");
        public List<WorkflowExecutionAgentActivationRequest> ActivationRequests { get; } = [];
        public WorkflowExecutionAgentCapabilities Capabilities => WorkflowExecutionAgentCapabilities.None;

        public ValueTask<IWorkflowExecutionAgent> GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default)
        {
            ActivationRequests.Add(request);
            Agent.AssignWorkflowExecutionId(request.WorkflowExecutionId);
            return ValueTask.FromResult<IWorkflowExecutionAgent>(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAgent(string workflowExecutionId) : IWorkflowExecutionAgent
    {
        private string _workflowExecutionId = workflowExecutionId;

        public WorkflowExecutionAgentDescriptor Descriptor { get; private set; } = NewDescriptor(workflowExecutionId);
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];

        public void AssignWorkflowExecutionId(string workflowExecutionId)
        {
            _workflowExecutionId = workflowExecutionId;
            Descriptor = NewDescriptor(workflowExecutionId);
        }

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: _workflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Accepted,
                recordedAt: DateTimeOffset.UtcNow));
        }

        private static WorkflowExecutionAgentDescriptor NewDescriptor(string workflowExecutionId) =>
            new(
                workflowExecutionId: workflowExecutionId,
                agentId: $"recording:{workflowExecutionId}",
                providerName: "Recording",
                status: WorkflowExecutionAgentStatus.Active,
                capabilities: WorkflowExecutionAgentCapabilities.None,
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

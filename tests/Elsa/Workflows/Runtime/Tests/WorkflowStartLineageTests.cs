using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowStartLineageTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RootRequest_DefaultsAuthorityFromValidatedRequestedBy()
    {
        var request = new WorkflowExecutionStartDispatchRequest("artifact-1", "initiator-1");

        Assert.Equal("initiator-1", request.Authority.SystemIdentity);
        Assert.Equal("initiator-1", request.Authority.RootInitiator);
        Assert.Null(request.Partition);
    }

    [Fact]
    public void LegacyStateSerialization_OmitsAbsentLineageFields()
    {
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: "legacy-1",
            PinnedExecutable: NewExecutable().Identity,
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

        var serialized = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("\"partition\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"authority\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_CarriesExplicitLineageAndAuthorityOutsideWorkflowInputs()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        var actorProvider = new RecordingActorProvider();
        var executable = NewExecutable();
        await executableStore.SaveAsync(executable);
        await sourceStore.SaveAsync(NewSourceReference());
        var dispatcher = new WorkflowStartDispatcher(
            executableStore,
            sourceStore,
            actorProvider,
            new FixedIdGenerator(),
            new FixedTimeProvider(Now),
            partitionAccessor: null);
        var authority = new WorkflowExecutionAuthoritySnapshot(
            systemIdentity: "parent-1",
            rootInitiator: "initiator-1",
            metadata: new Dictionary<string, string> { ["authority.source"] = "parent" });

        await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(
            artifactId: "artifact-1",
            requestedBy: "parent-1",
            workflowExecutionId: "child-1",
            idempotencyKey: "dispatch-start-1",
            metadata: null,
            variables: null,
            inputs: new Dictionary<string, object?> { ["message"] = "hello" },
            stimulusInput: null,
            triggerNodeId: null,
            runKind: WorkflowRunKind.PublishedRun,
            sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: "source-1"),
            provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
            parentWorkflowExecutionId: "parent-1",
            correlationId: "correlation-override",
            tenantId: "tenant-1",
            partition: new WorkflowExecutionPartition("partition-1"),
            authority: authority));

        var activation = Assert.Single(actorProvider.Activations);
        var envelope = Assert.Single(actorProvider.Actor.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal("partition-1", activation.Partition.Value);
        Assert.Equal("partition-1", envelope.Partition.Value);
        Assert.Equal("parent-1", payload.ParentWorkflowExecutionId);
        Assert.Equal("correlation-override", payload.CorrelationId);
        Assert.Equal("tenant-1", payload.TenantId);
        Assert.Equal("partition-1", payload.Partition!.Value);
        Assert.Equal(WorkflowRunKind.PublishedRun, payload.RunKind);
        Assert.Equal("parent-1", payload.Authority!.SystemIdentity);
        Assert.Equal("initiator-1", payload.Authority.RootInitiator);
        Assert.Equal("hello", payload.Inputs["message"].GetString());
        Assert.DoesNotContain("ParentWorkflowExecutionId", payload.Inputs.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("Authority", payload.Inputs.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task StartAndCheckpointHandlers_PersistExplicitLineageOnWorkflowState()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var executable = NewExecutable();
        await executableStore.SaveAsync(executable);
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: workflowStore,
            activityExecutionStateStore: activityStore,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        var startHandler = new WorkflowStartSchedulerWorkHandler(
            executableStore,
            queue,
            new FixedIdGenerator(),
            new FixedTimeProvider(Now));
        var checkpointHandler = new WorkflowCheckpointSchedulerWorkHandler(
            activityStore,
            new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), checkpointStore),
            inspectionAccumulator: null,
            timeProvider: new FixedTimeProvider(Now),
            workflowExecutionStateStore: workflowStore);
        var authority = new WorkflowExecutionAuthoritySnapshot("parent-1", "initiator-1");
        var payload = new WorkflowExecutionStartCommandPayload(
            pinnedExecutable: executable.Identity,
            requestedArtifactId: executable.Identity.ArtifactId,
            variables: null,
            inputs: null,
            stimulusInput: null,
            triggerNodeId: null,
            runKind: WorkflowRunKind.PublishedRun,
            pinnedSource: WorkflowExecutableSourceProvenance.From(NewSourceReference()),
            parentWorkflowExecutionId: "parent-1",
            correlationId: "correlation-1",
            tenantId: "tenant-1",
            partition: new WorkflowExecutionPartition("partition-1"),
            authority: authority);

        await startHandler.HandleAsync(NewStartWorkItem(payload));
        var checkpointWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("child-1")));
        await checkpointHandler.HandleAsync(checkpointWork);

        var state = await workflowStore.FindAsync("child-1");
        Assert.NotNull(state);
        Assert.Equal("parent-1", state.ParentWorkflowExecutionId);
        Assert.Equal("correlation-1", state.CorrelationId);
        Assert.Equal("tenant-1", state.TenantId);
        Assert.Equal("partition-1", state.Partition!.Value);
        Assert.Equal(WorkflowRunKind.PublishedRun, state.RunKind);
        Assert.Equal("parent-1", state.Authority!.SystemIdentity);
        Assert.Equal("initiator-1", state.Authority.RootInitiator);
    }

    private static RuntimeSchedulerWorkItem NewStartWorkItem(WorkflowExecutionStartCommandPayload payload) =>
        new(
            workItemId: "start-work",
            workflowExecutionId: "child-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.Start,
            envelopeId: "envelope-1",
            idempotencyKey: "dispatch-start-1",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 1,
            payload: JsonSerializer.SerializeToElement(payload));

    private static WorkflowExecutable NewExecutable() =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: "test/activity",
                activityTypeVersion: "1.0.0",
                descriptorType: "test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>());

    private static WorkflowExecutableSourceReference NewSourceReference() =>
        new(
            SourceReferenceId: "source-1",
            ArtifactId: "artifact-1",
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: Now,
            PublishedAt: Now,
            Scope: WorkflowExecutableReferenceScope.Published,
            ExpiresAt: null,
            PublicationId: "publication-1",
            SlotId: "slot-1");

    private sealed class RecordingActorProvider : IWorkflowExecutionActorProvider
    {
        public RecordingActor Actor { get; } = new();
        public List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(
            WorkflowExecutionActorActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            Activations.Add(request);
            Actor.SetExecution(request.WorkflowExecutionId);
            return ValueTask.FromResult<IWorkflowExecutionActor>(Actor);
        }

        public ValueTask PassivateAsync(
            WorkflowExecutionActorPassivationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingActor : IWorkflowExecutionActor
    {
        private string _workflowExecutionId = "unassigned";
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public WorkflowExecutionActorDescriptor Descriptor => new(
            _workflowExecutionId, $"actor:{_workflowExecutionId}", "Recording",
            WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.None, Now);

        public void SetExecution(string workflowExecutionId) => _workflowExecutionId = workflowExecutionId;

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default) => EnqueueAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchOptions options,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelope.EnvelopeId, envelope.WorkflowExecutionId,
                WorkflowExecutionCommandDispatchStatus.Accepted, Now));
        }
    }

    private sealed class FixedIdGenerator : IRuntimeExecutionIdGenerator
    {
        public string NewWorkflowExecutionId() => "generated-child";
        public string NewWorkflowExecutionCommandId() => "command-1";
        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-1";
        public string NewActivityExecutionId() => "activity-execution-root";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

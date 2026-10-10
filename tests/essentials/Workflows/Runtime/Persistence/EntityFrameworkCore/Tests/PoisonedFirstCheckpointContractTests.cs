using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeCheckpointCommitContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #2537: a poisoned first checkpoint leaves no workflow row, and the Entity Framework store refuses an incident-only
/// commit without one, so the execution used to be absent from every read model with no incident. Through the real
/// committer on every store, the poison observer must record the execution Faulted together with its blocking incident.
/// </summary>
public sealed class PoisonedFirstCheckpointContractTests
{
    private const string WorkItemId = "work-item-started";

    public static TheoryData<string> StoreData => RuntimeCheckpointCommitContract.StoreData();

    [Theory]
    [MemberData(nameof(StoreData))]
    public async Task A_poisoned_first_checkpoint_records_the_execution_faulted_with_its_incident(string store)
    {
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        await poisonStore.RecordAsync(new RuntimeSchedulerPoisonRecord(
            workflowExecutionId: WorkflowId,
            workItemId: WorkItemId,
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            handlerName: "WorkflowCheckpointSchedulerWorkHandler",
            fault: new RuntimeFaultInfo("System.InvalidOperationException", "Runtime coordination changed concurrently."),
            failureCount: 1,
            disposition: RuntimeSchedulerPoisonDisposition.Poisoned,
            firstFailedAt: OccurredAt,
            lastFailedAt: OccurredAt,
            nextRetryAt: null));
        var observer = new PoisonedSchedulerWorkIncidentObserver(
            poisonStore,
            backend.Incidents,
            backend.Committer,
            RuntimeCheckpointCommitContractBackend.Clock,
            activityExecutionStateStore: null,
            inspectionAccumulator: null,
            activityExecutionInspectionStore: null,
            workflowExecutionStateStore: backend.Executions,
            workflowDispatchStore: backend.Dispatches);

        await observer.OnDrainedAsync(StartEnvelope(), FaultedDrain());

        var execution = await backend.Executions.FindAsync(WorkflowId);
        Assert.NotNull(execution);
        Assert.Equal(WorkflowExecutionStatus.Faulted, execution!.Status);
        var incident = await backend.Incidents.FindAsync(WorkflowId, PoisonedSchedulerWorkIncidentObserver.IncidentId(WorkItemId));
        Assert.NotNull(incident);
        Assert.Equal(IncidentStatus.Blocking, incident!.Status);
        Assert.Equal(1, backend.Store.Calls);
    }

    private static RuntimeSchedulerDrainResult FaultedDrain() =>
        new(WorkflowId, OccurredAt, OccurredAt,
        [
            new RuntimeSchedulerWorkItemResult(
                workItemId: WorkItemId,
                workflowExecutionId: WorkflowId,
                commandKind: WorkflowExecutionCommandKind.Checkpoint,
                status: RuntimeSchedulerWorkItemResultStatus.Faulted,
                handlerName: "WorkflowCheckpointSchedulerWorkHandler",
                startedAt: OccurredAt,
                completedAt: OccurredAt,
                error: "System.InvalidOperationException: Runtime coordination changed concurrently.")
        ]);

    private static WorkflowExecutionCommandEnvelope StartEnvelope()
    {
        var pinned = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
        var payload = new WorkflowExecutionStartCommandPayload(
            pinnedExecutable: pinned,
            requestedArtifactId: pinned.ArtifactId,
            variables: null,
            inputs: null,
            stimulusInput: null,
            triggerNodeId: null,
            runKind: WorkflowRunKind.PublishedRun,
            pinnedSource: null,
            parentWorkflowExecutionId: null,
            correlationId: null,
            tenantId: null,
            partition: null,
            authority: null,
            startAuthority: null,
            dispatchNestingDepth: 0,
            testScope: null);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: WorkflowId,
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: OccurredAt,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-start",
            workflowExecutionId: WorkflowId,
            command: command,
            idempotencyKey: "idem-start",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: OccurredAt);
    }
}

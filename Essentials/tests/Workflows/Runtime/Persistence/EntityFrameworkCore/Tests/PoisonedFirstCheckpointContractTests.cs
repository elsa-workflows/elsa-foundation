using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

    /// <summary>Verifies that each checkpoint store persists a synthesized faulted execution with its incident.</summary>
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

    /// <summary>
    /// Reproduces #2537 at the root-write lease boundary: a WorkflowStarted checkpoint fails before writing state,
    /// the real drainer durably poisons it, and the observer restores execution and incident visibility atomically.
    /// </summary>
    [Fact]
    public async Task First_checkpoint_coordination_failure_is_poisoned_and_projected_durably()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options;
        await using var context = new RuntimeSqliteDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var access = new TenantAccessor();
        var codec = new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "poison-checkpoint-contract-signing-key-32" }));
        var executions = new EfWorkflowExecutionStateStore(context, access, codec);
        var incidents = new EfIncidentStateStore(context, access);
        var poison = new EfWorkflowSchedulerPoisonStore(context, access);
        var queue = new EfSchedulerWorkQueueStore(context, access, codec);
        var lease = new FirstWriteCoordinationConflict();
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            new EfRuntimeCheckpointCommitStore(context, access, RuntimeCheckpointCommitContractBackend.Clock, lease),
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        var envelope = StartEnvelope();
        var pinned = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.PinnedExecutable;
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(new WorkflowExecutable(
            identity: pinned,
            rootActivity: new ExecutableNode("node-root", "activity-root", "test/activity", "1.0.0",
                new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = "test" })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: OccurredAt,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference));
        var handler = new WorkflowCheckpointSchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(), committer, null, RuntimeCheckpointCommitContractBackend.Clock,
            executions, executables);
        var drainer = new WorkflowSchedulerDrainer(queue, [handler], executions, poison, new NoPauseGate(),
            RuntimeCheckpointCommitContractBackend.Clock, retryPolicy: new NoopRuntimeDomainRetryPolicy());
        await queue.EnqueueAsync(new RuntimeSchedulerWorkItem(
            WorkItemId, WorkflowId, envelope.Command.CommandId, WorkflowExecutionCommandKind.Checkpoint,
            envelope.EnvelopeId, "checkpoint-start", OccurredAt, OccurredAt,
            payload: JsonSerializer.SerializeToElement(new RuntimeCheckpointCommandPayload(
                pinned, RuntimeCheckpointNames.WorkflowStarted, [], RuntimeCheckpointCommandPayload.WorkflowStartReason))));

        var drain = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(WorkflowId));

        Assert.True(drain.StoppedOnFault);
        Assert.Equal(1, lease.Attempts);
        var record = Assert.Single(await poison.ListAsync(WorkflowId));
        Assert.Equal(WorkItemId, record.WorkItemId);
        Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, record.CommandKind);
        Assert.Equal(WorkflowCheckpointSchedulerWorkHandler.HandlerName, record.HandlerName);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.Fault.ExceptionType);
        Assert.Equal(FirstWriteCoordinationConflict.Message, record.Fault.Message);
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Equal(1, record.FailureCount);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowId)));
        Assert.Null(await executions.FindAsync(WorkflowId));
        Assert.Empty(await incidents.ListAsync(WorkflowId));
        Assert.Empty(await context.WorkflowRunHealthStates.AsNoTracking().ToListAsync());
        Assert.Empty(await context.RuntimeCheckpointCommits.AsNoTracking().ToListAsync());

        var observer = new PoisonedSchedulerWorkIncidentObserver(
            poison, incidents, committer, RuntimeCheckpointCommitContractBackend.Clock,
            activityExecutionStateStore: null, inspectionAccumulator: null, activityExecutionInspectionStore: null,
            workflowExecutionStateStore: executions);
        await observer.OnDrainedAsync(envelope, drain);
        await observer.OnDrainedAsync(envelope, drain); // A repeated notification must not duplicate the incident.

        // A new context reads the durable rows and projections, independently of the writer's tracked state.
        await using var verification = new RuntimeSqliteDbContext(options);
        var page = await new EfWorkflowExecutionStateStore(verification, access, codec)
            .QueryPageAsync(new WorkflowExecutionStatePageQuery(10, WorkflowExecutionId: WorkflowId));
        var execution = Assert.Single(page.Items);
        Assert.Equal(WorkflowExecutionStatus.Faulted, execution.Status);
        Assert.Equal(pinned, execution.PinnedExecutable);
        Assert.Equal(OccurredAt, execution.CompletedAt);
        var incident = Assert.Single(await new EfIncidentStateStore(verification, access).ListAsync(WorkflowId));
        Assert.Equal(PoisonedSchedulerWorkIncidentObserver.IncidentId(WorkItemId), incident.IncidentId);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentSeverity.Critical, incident.Severity);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, incident.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.PoisonedSchedulerWork, incident.ResolutionOutcome.SystemSource);
        Assert.Contains(FirstWriteCoordinationConflict.Message, incident.Message);
        var health = await verification.WorkflowRunHealthStates.AsNoTracking().SingleAsync();
        Assert.Equal((int)WorkflowExecutionStatus.Faulted, health.Status);
        Assert.Equal(1, health.IncidentCount);
        Assert.Equal(1, health.IncidentBearingCount);
        Assert.Single(await verification.RuntimeCheckpointCommits.AsNoTracking().ToListAsync());
        Assert.Single(await new EfWorkflowSchedulerPoisonStore(verification, access).ListAsync(WorkflowId));
        Assert.Equal(2, lease.Attempts);
    }

    /// <summary>Replays the exhausted historical coordination retry before the first checkpoint write callback.</summary>
    private sealed class FirstWriteCoordinationConflict : IWorkflowExecutableRootWriteLeaseManager
    {
        public const string Message = "Runtime coordination changed concurrently.";
        public int Attempts { get; private set; }

        /// <summary>Fails the first lease acquisition and allows the observer's recovery checkpoint to persist.</summary>
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default)
        {
            if (++Attempts == 1)
                throw new InvalidOperationException(Message);
            return write(cancellationToken);
        }
    }

    /// <summary>Keeps this failure-path test independent of pause policy.</summary>
    private sealed class NoPauseGate : IWorkflowSchedulerPauseGate
    {
        /// <summary>Allows the queued checkpoint to reach its handler.</summary>
        public ValueTask<SchedulerPauseDecision?> EvaluateAsync(RuntimeSchedulerWorkItem workItem,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<SchedulerPauseDecision?>(null);
    }

    /// <summary>Uses the same tenant scope for the writer and independent verification context.</summary>
    private sealed class TenantAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(Tenant));
    }

    /// <summary>Models the drain result paired with the explicitly seeded poison record.</summary>
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

    /// <summary>Builds the root start envelope from which the observer can recover the missing execution identity.</summary>
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

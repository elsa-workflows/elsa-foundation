using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.WorkHandlers;

public sealed class WorkflowScheduleActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowScheduleActivitySchedulerWorkHandler);
    private const string SelectedChildScheduleReceiptKey = "runtime.selectedChildScheduleHash";

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly RuntimeCheckpointCommitter? _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowExecutableReader? _executableReader;
    private readonly ReplaySafeFusionDriver? _fusionDriver;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly ILogger _logger;

    public WorkflowScheduleActivitySchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeCheckpointCommitter? checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider,
        IWorkflowExecutableReader? executableReader = null,
        ReplaySafeFusionDriver? fusionDriver = null,
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        ILogger<WorkflowScheduleActivitySchedulerWorkHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutableStore = workflowExecutableStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _timeProvider = timeProvider;
        _executableReader = executableReader;
        _fusionDriver = fusionDriver;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity;
    }

    /// <summary>Direct (no-pipeline) dispatch: run the handler and commit its checkpoint inline (when one is produced).</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        var commit = await ExecuteAsync(workItem, cancellationToken);
        if (commit is not null)
            await _checkpointCommitter!.CommitAsync(commit, cancellationToken);
    }

    /// <summary>Pipeline dispatch (Move 2): run the handler in the Invoke slot and stage its commit for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        var commit = await ExecuteAsync(workItem, cancellationToken);
        if (commit is not null)
            pipelineContext.Workspace.StageCheckpointCommit(commit);
    }

    private static string ChildScheduleReceipt(RuntimeSchedulerWorkItem work) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            work.WorkflowExecutionId, work.WorkItemId, work.CommandId, work.ExecutionScopeId, work.Attempt, work.Payload
        })));

    private async ValueTask RequireOwnedChildAsync(RuntimeSchedulerWorkItem work, RuntimeScheduleActivityCommandPayload payload,
        WorkflowExecutionState workflow, WorkflowExecutable executable, RuntimeTestContextState context, CancellationToken cancellationToken)
    {
        var parent = await _activityExecutionStateStore.FindAsync(work.WorkflowExecutionId, payload.ParentActivityExecutionId!, cancellationToken)
            ?? throw new InvalidOperationException("The selected child has no actual parent invocation.");
        await RuntimeTestContextOwner.RequireConsumerAsync(workflow, parent, _activityExecutionStateStore, cancellationToken);
        if (parent.Status is not (ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting or ActivityExecutionStatus.Suspended) ||
            payload.ActivityExecutionId == context.RootActivityExecutionId ||
            !context.OwnedNodeIds.Contains(payload.ExecutableNodeId, StringComparer.Ordinal) ||
            !executable.NodesById[parent.Execution.ExecutableNodeId].ChildSlots.SelectMany(x => x.Activities)
                .Any(x => x.ExecutableNodeId == payload.ExecutableNodeId) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workflow.PinnedExecutable, payload.PinnedExecutable) ||
            payload.SchedulingProvenance.ParentActivityExecutionId != payload.ParentActivityExecutionId ||
            payload.SchedulingProvenance.SchedulingActivityExecutionId != payload.SchedulingActivityExecutionId ||
            payload.SchedulingProvenance.SchedulingWorkflowExecutionId != work.WorkflowExecutionId ||
            payload.SchedulingProvenance.ExecutionScopeId != work.ExecutionScopeId ||
            work.ExecutionScopeId != (parent.ExecutionScopeId ?? parent.Provenance.ExecutionScopeId) ||
            NormalizeProvenance(work.WorkflowExecutionId, payload).ExecutionScopeId != work.ExecutionScopeId)
            throw new InvalidOperationException("The selected child differs from its actual owned parent relation.");
        var scheduler = payload.SchedulingActivityExecutionId is { } id
            ? await _activityExecutionStateStore.FindAsync(work.WorkflowExecutionId, id, cancellationToken) : null;
        if (scheduler is null) throw new InvalidOperationException("The selected child has no actual scheduling invocation.");
        await RuntimeTestContextOwner.RequireConsumerAsync(workflow, scheduler, _activityExecutionStateStore, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit?> ExecuteAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var schedulePayload = DeserializeSchedulePayload(workItem);
        var workflow = _workflowExecutionStateStore is { } workflowStore
            ? await workflowStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken) : null;

        // R-7 (spec 261): a delayed schedule reaches its run some time after the parent staged it. By then the run may be
        // gone or terminal, or the parent may have finished; such a schedule is acknowledged and schedules nothing.
        if (RuntimeDelayedSchedules.ReadNotBefore(workItem) is { } notBefore &&
            await DelayedScheduleRefusalAsync(workItem, schedulePayload, workflow, cancellationToken) is { } refusal)
        {
            _logger.LogInformation(
                "Acknowledged delayed schedule {WorkItemId} of node {ExecutableNodeId} for workflow execution {WorkflowExecutionId} (due {NotBefore}) without scheduling it: {Reason}.",
                workItem.WorkItemId, schedulePayload.ExecutableNodeId, workItem.WorkflowExecutionId, notBefore, refusal);
            return null;
        }

        var executable = await PinnedExecutableRead.FindAsync(_executableReader, _workflowExecutableStore, schedulePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(schedulePayload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, schedulePayload.PinnedExecutable, executable.Identity);

        if (workItem.ExecutionScopeId is { } executionScopeId)
        {
            var scopeOwner = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, executionScopeId, cancellationToken);
            if (scopeOwner?.Status == ActivityExecutionStatus.Cancelled)
                return null;
        }

        var executableNode = SchedulerWorkHandlerHelpers.ResolveExecutableNode(workItem, executable, schedulePayload.ExecutableNodeId, "ScheduleActivity");

        var existing = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, schedulePayload.ActivityExecutionId, cancellationToken);
        if (workflow is not null &&
            RuntimeTestContextOwner.RequireContext(workflow) is { } context)
        {
            if (schedulePayload.ParentActivityExecutionId is not null)
            {
                if (existing is { Status: ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or
                    ActivityExecutionStatus.Recovered or ActivityExecutionStatus.Superseded })
                {
                    if (existing.Execution.ExecutableNodeId != schedulePayload.ExecutableNodeId ||
                        existing.ParentActivityExecutionId != schedulePayload.ParentActivityExecutionId ||
                        existing.SchedulingActivityExecutionId != schedulePayload.SchedulingActivityExecutionId ||
                        !existing.Metadata.TryGetValue(SelectedChildScheduleReceiptKey, out var receipt) || receipt != ChildScheduleReceipt(workItem))
                        throw new InvalidOperationException("A settled selected child cannot reconcile a changed schedule.");
                    // No work is emitted. Retained exact schedules may be acknowledged after their frames close.
                    await RuntimeTestContextOwner.RequireConsumerAsync(workflow, existing, _activityExecutionStateStore, cancellationToken, requireActive: false);
                    return null;
                }
                await RequireOwnedChildAsync(workItem, schedulePayload, workflow, executable, context, cancellationToken);
            }
            else if (schedulePayload.ActivityExecutionId != context.RootActivityExecutionId ||
                schedulePayload.ExecutableNodeId != workflow.TestEntry!.ExecutableNodeId ||
                schedulePayload.ParentActivityExecutionId is not null || schedulePayload.SchedulingActivityExecutionId is not null ||
                schedulePayload.IterationFrame is not null || workItem.ExecutionScopeId is not null ||
                schedulePayload.Reason != RuntimeScheduleActivityCommandPayload.WorkflowStartReason ||
                schedulePayload.SchedulingProvenance is not { ParentActivityExecutionId: null, SchedulingActivityExecutionId: null,
                    BranchId: null, IterationId: null, ExecutionPathId: null, ExecutionScopeId: null } ||
                schedulePayload.SchedulingProvenance.SchedulingWorkflowExecutionId != workItem.WorkflowExecutionId ||
                schedulePayload.SchedulingProvenance.SchedulingCause != RuntimeScheduleActivityCommandPayload.WorkflowStartReason ||
                schedulePayload.SchedulingProvenance.Metadata.Count != 0 ||
                schedulePayload.SchedulingProvenance.Attempt != workItem.Attempt ||
                workItem.Attempt is not { AttemptNumber: 1, PreviousAttemptActivityExecutionId: null } ||
                workItem.Attempt.FirstAttemptActivityExecutionId != context.RootActivityExecutionId ||
                !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workflow.PinnedExecutable, schedulePayload.PinnedExecutable))
                throw new InvalidOperationException("The schedule is outside the exact admitted selected invocation.");
        }

        if (existing is not null)
        {
            if (!StringComparer.Ordinal.Equals(existing.Execution.ExecutableNodeId, schedulePayload.ExecutableNodeId))
                throw new InvalidOperationException($"ScheduleActivity scheduler work item '{workItem.WorkItemId}' references executable node '{schedulePayload.ExecutableNodeId}', but activity execution '{schedulePayload.ActivityExecutionId}' belongs to executable node '{existing.Execution.ExecutableNodeId}'.");

            if (existing.Status == ActivityExecutionStatus.Scheduled)
                await EnqueueStartActivityAsync(workItem, schedulePayload, cancellationToken);
            else if (existing.Status == ActivityExecutionStatus.Running &&
                     executableNode.IntrinsicKind is null &&
                     executableNode.ActivityContract?.SideEffectProfile == SideEffectProfile.ReplaySafe)
            {
                if (existing.InputSnapshot is null)
                    throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{existing.InvocationId}' has no committed input snapshot.");

                // A durable fused Schedule anchor may be redelivered after ActivityStarted was folded into a
                // continuing cap/attempt checkpoint. Re-enter the existing deterministic Start -> Invoke path; the
                // Start handler observes Running and enqueues the already-materialized invocation from its snapshot.
                await EnqueueStartActivityAsync(workItem, schedulePayload, cancellationToken);
            }

            return null;
        }

        var state = NewActivityExecutionState(workItem, schedulePayload, executableNode);
        if (workflow?.TestContext is not null && schedulePayload.ParentActivityExecutionId is not null)
        {
            var metadata = state.Metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            metadata[SelectedChildScheduleReceiptKey] = ChildScheduleReceipt(workItem);
            state = state with { Metadata = RuntimeModelMetadata.Snapshot(metadata) };
        }
        if (_checkpointCommitter is null || _inspectionAccumulator is null)
        {
            await _activityExecutionStateStore.SaveAsync(state, cancellationToken);
            await EnqueueStartActivityAsync(workItem, schedulePayload, cancellationToken);
            return null;
        }

        // When this fresh schedule targets a ReplaySafe leaf inside a live coalescing burst, fuse the
        // schedule → start → invoke stages into this one dispatch. Commit the intent-free ActivityScheduled checkpoint
        // and run the remaining stages inline, so the span's StartActivity/InvokeActivity work items are never enqueued.
        // Return null so neither dispatch path commits anything further; the invoke stage's terminal commit carries the
        // completion cascade continuation via the normal overlay outbox. Every other case takes the discrete chain.
        if (_fusionDriver is { } driver && driver.ShouldFuse(workItem.WorkflowExecutionId, executableNode))
        {
            var core = await BuildScheduledCommitAsync(workItem, schedulePayload, state, cancellationToken);
            driver.RegisterFusedScheduleAnchor(workItem);
            await _checkpointCommitter.CommitAsync(core.Commit, cancellationToken);
            await driver.ContinueFusedScheduleSpanAsync(workItem, core.StartWorkItem, cancellationToken);
            return null;
        }

        return await NewCommitAsync(workItem, schedulePayload, state, cancellationToken);
    }

    // Why a delivered delayed schedule must not schedule its child, or null when it may. The parent is the activity the
    // payload names as parent; it staged the schedule while running and must still be running.
    private async ValueTask<string?> DelayedScheduleRefusalAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        WorkflowExecutionState? run,
        CancellationToken cancellationToken)
    {
        if (_workflowExecutionStateStore is not null)
        {
            if (run is null)
                return "the workflow execution no longer exists";
            if (run.Status.IsTerminal())
                return $"the workflow execution is {run.Status}";
        }

        var parentId = schedulePayload.ParentActivityExecutionId ?? schedulePayload.SchedulingActivityExecutionId;
        if (parentId is null)
            return null;
        var parent = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, parentId, cancellationToken);
        if (parent is null)
            return $"its parent activity execution '{parentId}' no longer exists";
        return parent.Status == ActivityExecutionStatus.Running ? null : $"its parent activity execution '{parentId}' is {parent.Status}";
    }

    private static RuntimeScheduleActivityCommandPayload DeserializeSchedulePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "ScheduleActivity scheduler work item requires a schedule activity payload.",
            resolvedToNullMessage: "ScheduleActivity scheduler work item payload resolved to null.",
            invalidPayloadMessage: "ScheduleActivity scheduler work item payload is not a valid schedule activity payload.",
            deserialize: static (_, payload) => payload.Deserialize<RuntimeScheduleActivityCommandPayload>(),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException ||
                exception is ArgumentException argumentException && IsSchedulePayloadValidationException(argumentException));

    private static bool IsSchedulePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason" or
            "schedulingActivityExecutionId" or
            "parentActivityExecutionId";

    private ActivityExecutionState NewActivityExecutionState(
        RuntimeSchedulerWorkItem workItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        ExecutableNode executableNode)
    {
        var scheduledAt = _timeProvider.GetUtcNow();
        var provenance = NormalizeProvenance(workItem.WorkflowExecutionId, schedulePayload);
        var opensExecutionScope = executableNode.Metadata.ContainsKey("activity.definitionVersionId");
        var executionScopeId = opensExecutionScope
            ? schedulePayload.ActivityExecutionId
            : provenance.ExecutionScopeId;
        var enclosingExecutionScopeId = opensExecutionScope &&
                                        provenance.ExecutionScopeId is { } enclosingScopeId &&
                                        !StringComparer.Ordinal.Equals(enclosingScopeId, schedulePayload.ActivityExecutionId)
            ? enclosingScopeId
            : null;
        if (opensExecutionScope)
            provenance = provenance with { ExecutionScopeId = executionScopeId };
        var execution = new ActivityExecution(
            ActivityExecutionId: schedulePayload.ActivityExecutionId,
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            ExecutableNodeId: executableNode.ExecutableNodeId,
            AuthoredActivityId: executableNode.AuthoredActivityId,
            ActivityType: executableNode.ActivityType,
            ActivityTypeVersion: executableNode.ActivityTypeVersion);

        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.ScheduleReason] = schedulePayload.Reason,
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.PinnedArtifactId] = schedulePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.PinnedArtifactVersion] = schedulePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.PinnedArtifactHash] = schedulePayload.PinnedExecutable.ArtifactHash
        };
        foreach (var key in BoundaryInspectionMetadataKeys)
            if (executableNode.Metadata.TryGetValue(key, out var value))
                metadata[key] = value;

        return new ActivityExecutionState(
            Execution: execution,
            Status: ActivityExecutionStatus.Scheduled,
            SubStatus: null,
            ExecutionSequence: ResolveExecutionSequence(workItem, schedulePayload.ActivityExecutionId),
            ScheduledAt: scheduledAt,
            StartedAt: null,
            CompletedAt: null,
            SchedulingActivityExecutionId: provenance.SchedulingActivityExecutionId,
            ParentActivityExecutionId: provenance.ParentActivityExecutionId,
            BranchId: provenance.BranchId,
            IterationId: provenance.IterationId,
            Provenance: provenance,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: metadata,
            DocumentVersion: ActivityExecutionValueFlowDocumentVersions.Current,
            ContractIdentity: executableNode.IntrinsicKind is null
                ? new ActivityInvocationContractIdentity(
                    executableNode.ActivityType,
                    executableNode.ActivityTypeVersion,
                    schedulePayload.PinnedExecutable.ArtifactHash)
                : null,
            Attempts: [],
            TriggerRegistrations: [],
            TriggerDeliveries: [],
            ValueFlowCompatibility: ActivityExecutionValueFlowDocumentVersionGuard.Compatible(
                ActivityExecutionValueFlowDocumentVersions.Current,
                ActivityExecutionValueFlowDocumentVersions.Current))
        {
            IterationFrameRequest = schedulePayload.IterationFrame,
            ExecutionScopeId = executionScopeId,
            EnclosingExecutionScopeId = enclosingExecutionScopeId,
            Attempt = provenance.Attempt
        };
    }

    private static readonly string[] BoundaryInspectionMetadataKeys =
    [
        "activity.definitionId",
        "activity.definitionVersionId",
        "activity.version",
        "activity.templateHash",
        "activity.sourceReferenceId",
        "activity.invocationOrigin",
        "activity.placementNamespace",
        "graph.templateHash",
        // The boundary's state carries the closed-scope marker, so a body node's ancestor walk finds its
        // nearest closed scope from durable state alone.
        RuntimeMetadataKeys.ActivityExecutionScope
    ];

    /// <summary>
    /// Discrete adapter: builds the fused-mode <c>ActivityScheduled</c> commit core and re-attaches the
    /// <c>StartActivity</c> continuation intent, reproducing today's commit byte-for-byte. The commit builder and the
    /// derived <c>StartActivity</c> work item are extracted into <see cref="BuildScheduledCommitAsync"/> so the
    /// fusion driver can commit the same stage without the continuation intent and run the next stage inline instead of
    /// enqueuing it.
    /// </summary>
    private async ValueTask<RuntimeCheckpointCommit> NewCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        var core = await BuildScheduledCommitAsync(workItem, schedulePayload, state, cancellationToken);
        return core.Commit with
        {
            PostCommitIntents =
            [
                SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                    workItem, schedulePayload.ActivityExecutionId, core.StartWorkItem, core.OccurredAt)
            ]
        };
    }

    /// <summary>
    /// The <c>ActivityScheduled</c> stage core: produces the <c>ActivityScheduled</c> checkpoint
    /// commit <b>without</b> its <c>StartActivity</c> post-commit intent, alongside the derived <c>StartActivity</c>
    /// work item and the checkpoint's occurrence time. The discrete handler re-attaches the intent
    /// (<see cref="NewCommitAsync"/>); the fused driver commits the intent-free commit and dispatches the returned work
    /// item inline. Reused by both — never re-implemented — so the two paths stay byte-identical by construction.
    /// </summary>
    internal async ValueTask<ScheduledCommitCore> BuildScheduledCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:activity-scheduled:{schedulePayload.ActivityExecutionId}";
        var metadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = schedulePayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = schedulePayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = schedulePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = schedulePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = schedulePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = schedulePayload.PinnedExecutable.ArtifactHash
        });
        var inspection = await _inspectionAccumulator!.BuildProjectionAsync(state, checkpointId, occurredAt, metadata: metadata, cancellationToken: cancellationToken);
        var startWorkItem = NewStartActivityWorkItem(workItem, schedulePayload);

        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:activity-scheduled:{schedulePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityScheduled,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [schedulePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: schedulePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: state,
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: schedulePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: [],
            Metadata: metadata);

        return new ScheduledCommitCore(commit, startWorkItem, occurredAt);
    }

    /// <summary>
    /// The intent-free <c>ActivityScheduled</c> commit plus the derived <c>StartActivity</c> continuation work item and
    /// the checkpoint's occurrence time.
    /// </summary>
    internal readonly record struct ScheduledCommitCore(
        RuntimeCheckpointCommit Commit,
        RuntimeSchedulerWorkItem StartWorkItem,
        DateTimeOffset OccurredAt);

    private async ValueTask EnqueueStartActivityAsync(
        RuntimeSchedulerWorkItem scheduleWorkItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        CancellationToken cancellationToken)
    {
        var workItem = NewStartActivityWorkItem(scheduleWorkItem, schedulePayload);
        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private RuntimeSchedulerWorkItem NewStartActivityWorkItem(
        RuntimeSchedulerWorkItem scheduleWorkItem,
        RuntimeScheduleActivityCommandPayload schedulePayload)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeStartActivityCommandPayload(
            schedulePayload.PinnedExecutable,
            schedulePayload.ExecutableNodeId,
            schedulePayload.ActivityExecutionId,
            RuntimeStartActivityCommandPayload.ScheduledActivityReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(scheduleWorkItem.WorkItemId, $"start:{schedulePayload.ActivityExecutionId}"),
            workflowExecutionId: scheduleWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(scheduleWorkItem.CommandId, $"start:{schedulePayload.ActivityExecutionId}"),
            commandKind: WorkflowExecutionCommandKind.StartActivity,
            envelopeId: scheduleWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(scheduleWorkItem.IdempotencyKey, $"start:{schedulePayload.ActivityExecutionId}"),
            enqueuedAt: now,
            recordedAt: now,
            sequence: scheduleWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            // The due time of a delayed schedule belongs to the schedule alone; the started child must not inherit it.
            commandMetadata: WithoutScheduleNotBefore(scheduleWorkItem.CommandMetadata),
            envelopeMetadata: scheduleWorkItem.EnvelopeMetadata,
            executionScopeId: scheduleWorkItem.ExecutionScopeId ?? schedulePayload.SchedulingProvenance.ExecutionScopeId,
            attempt: scheduleWorkItem.Attempt ?? schedulePayload.SchedulingProvenance.Attempt);
    }

    private static IReadOnlyDictionary<string, string> WithoutScheduleNotBefore(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.ContainsKey(RuntimeMetadataKeys.ScheduleNotBefore))
            return metadata;
        return metadata
            .Where(item => !StringComparer.Ordinal.Equals(item.Key, RuntimeMetadataKeys.ScheduleNotBefore))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }

    private static ActivitySchedulingProvenance NormalizeProvenance(
        string workflowExecutionId,
        RuntimeScheduleActivityCommandPayload schedulePayload)
    {
        var provenance = schedulePayload.SchedulingProvenance;
        return ActivitySchedulingProvenance.From(
            workflowExecutionId,
            provenance.ParentActivityExecutionId ?? schedulePayload.ParentActivityExecutionId,
            provenance.SchedulingActivityExecutionId ?? schedulePayload.SchedulingActivityExecutionId,
            provenance.BranchId,
            provenance.IterationId,
            provenance.ExecutionPathId,
            provenance.ExecutionScopeId ?? ReadMetadata(provenance.Metadata, RuntimeMetadataKeys.FlowchartExecutionScopeId),
            provenance.SchedulingCause ?? schedulePayload.Reason,
            provenance.Metadata);
    }

    private static string? ReadMetadata(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static long ResolveExecutionSequence(RuntimeSchedulerWorkItem workItem, string activityExecutionId) =>
        workItem.Sequence ?? StableHash(activityExecutionId);

    private static long StableHash(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return (long)(hash & long.MaxValue);
    }
}

using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Handles a non-terminal child→parent structural notification (spec 126, seam C). Reactivates the target
/// parent — the notifying child's committed parent — from its pinned snapshot and dispatches
/// <see cref="IRuntimeActivityChildNotificationHandler.OnChildNotifiedAsync"/>, applying the returned
/// continuation (and any seam-A subtree cancellations / child schedules / further parent notifications it
/// staged) in one atomic checkpoint commit. The notifying child keeps running throughout; a notification whose
/// child has since completed or faulted still delivers (only the parent's non-Running state acks it away), and
/// a parent that does not implement the interface acks the notification silently. Sibling of
/// <see cref="WorkflowParentActivityCompletionSchedulerWorkHandler"/>.
/// </summary>
public sealed class WorkflowNotifyParentActivitySchedulerWorkHandler : RuntimeSchedulerWorkHandlerBase<RuntimeNotifyParentCommandPayload>
{
    // FROZEN wire value (spec 126 D2): the drainer's poison path persists this HandlerName verbatim and matches
    // it on recovery. Do not rename the type or this constant.
    public const string HandlerName = nameof(WorkflowNotifyParentActivitySchedulerWorkHandler);

    public WorkflowNotifyParentActivitySchedulerWorkHandler(
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider? timeProvider = null)
        : base(serviceScopeFactory, timeProvider)
    {
    }

    public override string Name => HandlerName;

    public override bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return workItem.CommandKind == WorkflowExecutionCommandKind.NotifyParentActivity;
    }

    protected override async ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeNotifyParentCommandPayload payload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();

        var executable = await PinnedExecutableRead.FindAsync(serviceProvider, payload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(payload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, payload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(payload.ExecutableNodeId, out var parentExecutableNode))
            throw new InvalidOperationException($"NotifyParentActivity scheduler work item '{workItem.WorkItemId}' references parent executable node '{payload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var parentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        if (parentState is null)
            throw new InvalidOperationException($"NotifyParentActivity scheduler work item '{workItem.WorkItemId}' references missing parent activity execution '{payload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(parentState.Execution.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"NotifyParentActivity scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but parent activity execution '{payload.ActivityExecutionId}' belongs to executable node '{parentState.Execution.ExecutableNodeId}'.");

        parentState.EnsureValueFlowCompatible();

        // spec 126 FR-4: a parent that is no longer running acks the notification away (late-absorption, mirroring
        // the completion path). The notifying child's own status is NOT a delivery gate — a child that completed
        // or faulted after staging still delivers here.
        if (parentState.Status != ActivityExecutionStatus.Running)
            return;

        var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();
        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var checkpointCommitter = serviceProvider.GetService<RuntimeCheckpointCommitter>()
            ?? throw new InvalidOperationException("A checkpoint committer is required to durably claim a structural notification callback activation attempt.");
        var inspectionAccumulator = serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>();
        var activityFaultIncidentRecorder = serviceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var payloadCapturePolicy = serviceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();
        var scopeService = new RuntimeContainerScopeService(
            activityExecutionStateStore,
            serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>());

        SimpleActivityExecutionContext? context = null;
        RuntimeStructuralContinuation? continuation = null;
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> subtreeCancellationPlans = [];
        IReadOnlyCollection<RuntimeSchedulerWorkItem> parentNotificationWorkItems = [];
        ActivityExecutionState? callbackBypassParentState = null;
        ActivityActivationLease? activationLease = null;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots = [];
        RuntimeActivityCompletionCheckpointPreparation? completionCheckpointPreparation = null;
        try
        {
            var activationClaim = await ActivityAttemptActivationClaimer.ClaimStructuralCallbackAsync(
                checkpointCommitter,
                TimeProvider,
                workItem,
                payload.PinnedExecutable,
                payload.ExecutableNodeId,
                payload.ActivityExecutionId,
                payload.Reason,
                parentState,
                parentExecutableNode.ActivityContract!.SideEffectProfile,
                cancellationToken);
            parentState = activationClaim.State;
            var constructedParent = await StructuralParentEvaluationSupport.ConstructActivityAsync(
                serviceProvider,
                parentExecutableNode,
                parentState,
                cancellationToken);
            activationLease = constructedParent.ActivationLease;
            valueSnapshots = ActivityExecutionInspection.BuildInputValueSnapshots(
                payloadCapturePolicy,
                workItem,
                payload.ActivityExecutionId,
                payload.ExecutableNodeId,
                parentExecutableNode.ActivityContract!,
                constructedParent.InputSnapshot,
                RuntimeMetadataKeys.NotifyParentSchedulerWorkItemId,
                TimeProvider.GetUtcNow());
            var parentActivity = constructedParent.Activity;

            // spec 126 D3: the seam is opt-in by interface. A parent that does not implement the notification
            // handler acks the notification without invoking any callback and without fault — end the attempt the
            // claim opened and return the parent to its prior deferred state.
            if (parentActivity is not IRuntimeActivityChildNotificationHandler notificationHandler)
            {
                callbackBypassParentState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                    parentState,
                    Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend,
                    TimeProvider.GetUtcNow());
            }
            else
            {
                var liveChildActivities = parentActivity is IRuntimeLiveChildActivityConsumer
                    ? await StructuralParentEvaluationSupport.LoadLiveChildActivitiesAsync(activityExecutionStateStore, workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken)
                    : [];
                var scopedVariableEnvelopes = await scopeService.ProjectScopedVariablesForReaderAsync(
                    parentActivity, executable, parentState, cancellationToken);

                context = SimpleActivityExecutionContext.ForExecution(
                    parentActivity,
                    cancellationToken,
                    workItem.WorkflowExecutionId,
                    payload.PinnedExecutable,
                    workItem,
                    parentExecutableNode,
                    parentState,
                    variableScope: null,
                    liveChildActivities: liveChildActivities,
                    scopedVariableEnvelopes: scopedVariableEnvelopes);

                var notifiedContext = new ActivityChildNotifiedContext(
                    payload.NotifyingChildActivityExecutionId,
                    payload.NotifyingChildExecutableNodeId,
                    payload.Code,
                    payload.NotifyingChildIterationId,
                    payload.Payload);

                continuation = await notificationHandler.OnChildNotifiedAsync(context, notifiedContext);

                var scheduledChildren = context.GetChildActivityScheduleRequests();
                if (!continuation.IsDeferred && scheduledChildren.Count > 0)
                    throw new InvalidOperationException("A terminal structural decision cannot also schedule child activities in the same child-notification evaluation.");

                // spec 126 FR-5: seam-B fault absorption stays a child-fault-evaluation-only power.
                if (context.GetChildFaultAbsorptionRequests().Count > 0)
                    throw new InvalidOperationException("A child-fault absorption is only valid in a child-fault evaluation.");

                subtreeCancellationPlans = await StructuralParentEvaluationSupport.PlanChildSubtreeCancellationsAsync(
                    serviceProvider,
                    activityExecutionStateStore,
                    TimeProvider,
                    workItem,
                    payload.ActivityExecutionId,
                    context.GetChildSubtreeCancellationRequests(),
                    continuation,
                    cancellationToken);

                // The notification consumer may itself notify ITS parent (bubbling is the consumer's recursion).
                parentNotificationWorkItems = await ParentNotificationEvaluation.BuildAsync(
                    activityExecutionStateStore,
                    TimeProvider,
                    workItem,
                    payload.PinnedExecutable,
                    parentState,
                    context.GetParentNotificationRequests(),
                    continuation,
                    cancellationToken);

                if (continuation.IsComplete && parentActivity is IRuntimeActivityCheckpointParticipant checkpointParticipant)
                {
                    var persistedValues = await durableValueStateStore.ListAllDurableValueStatesAsync(workItem.WorkflowExecutionId, cancellationToken);
                    completionCheckpointPreparation = await checkpointParticipant.PrepareCompletionCheckpointAsync(
                        context,
                        persistedValues,
                        TimeProvider.GetUtcNow(),
                        cancellationToken);
                    var completionTransition = (IActivityCompletionTransition)completionCheckpointPreparation.Transition;
                    if (!StringComparer.Ordinal.Equals(completionTransition.Outcome, continuation.OutcomeName))
                        throw new InvalidOperationException(
                            $"Checkpoint participant completion outcome '{completionTransition.Outcome}' does not match structural continuation outcome '{continuation.OutcomeName}'.");
                }
            }
        }
        catch (OperationCanceledException cancellationException) when (cancellationToken.IsCancellationRequested)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            if (disposalException is not null)
                throw new AggregateException("Structural notification callback cancellation and activation disposal both failed.", cancellationException, disposalException);
            throw;
        }
        catch (Exception exception)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            var fault = disposalException is null ? exception : ActivityActivationLeaseDisposer.Combine(exception, disposalException);
            var subStatus = disposalException is null ? "ParentNotificationFaulted" : "ActivityDisposalFailed";
            await RecordParentFaultAsync(
                activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter,
                workItem, payload, parentState, fault, subStatus, valueSnapshots, cancellationToken);
            return;
        }

        var activationDisposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
        activationLease = null;
        if (activationDisposalException is not null)
        {
            await RecordParentFaultAsync(
                activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter,
                workItem, payload, parentState, activationDisposalException, "ActivityDisposalFailed", valueSnapshots, cancellationToken);
            return;
        }

        if (callbackBypassParentState is not null)
        {
            await StructuralParentEvaluationSupport.CommitDeferredParentActivityAsync(
                checkpointCommitter, inspectionAccumulator, idGenerator, TimeProvider, NewCommitSource(workItem, payload, "ChildNotificationEvaluation"),
                callbackBypassParentState, [], [], [], subtreeCancellationPlans, valueSnapshots, cancellationToken);
            return;
        }

        var resolvedContext = context ?? throw new InvalidOperationException("Structural notification callback did not create an execution context.");
        var resolvedContinuation = continuation ?? throw new InvalidOperationException("Structural notification callback did not return a continuation.");
        var currentParentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken) ?? parentState;
        if (currentParentState.Status != ActivityExecutionStatus.Running)
            return;

        currentParentState = RuntimeStructuralStateProjector.Apply(currentParentState, resolvedContinuation, TimeProvider.GetUtcNow());

        if (resolvedContinuation.Kind == RuntimeStructuralContinuationKind.Fault)
        {
            var fault = resolvedContinuation.Fault!;
            var faultedParentState = currentParentState with
            {
                Fault = fault.ToNormalized()
            };
            var exception = new ActivityTransitionFaultException(fault);
            var request = NewFaultIncidentRecordRequest(checkpointCommitter, workItem, payload, faultedParentState, exception, "ActivityReturnedFault", valueSnapshots);
            var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, payload.ActivityExecutionId, "ActivityReturnedFault");
            var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
                activityExecutionStateStore, TimeProvider, workItem, payload.PinnedExecutable, faultedParentState, incidentId, cancellationToken);
            await activityFaultIncidentRecorder.CommitAsync(
                parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
                cancellationToken);
            return;
        }

        if (resolvedContinuation.Kind == RuntimeStructuralContinuationKind.Cancel)
        {
            await ActivityCancellationCheckpointService.CommitAsync(
                checkpointCommitter, inspectionAccumulator, TimeProvider, workItem,
                currentParentState, resolvedContinuation.CancellationReason!, valueSnapshots, cancellationToken: cancellationToken);
            return;
        }

        var childScheduleRequests = resolvedContext.GetChildActivityScheduleRequests();
        if (resolvedContinuation.IsDeferred)
        {
            currentParentState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                currentParentState, Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, TimeProvider.GetUtcNow());
            await StructuralParentEvaluationSupport.CommitDeferredParentActivityAsync(
                checkpointCommitter, inspectionAccumulator, idGenerator, TimeProvider, NewCommitSource(workItem, payload, "ChildNotificationEvaluation"),
                currentParentState, [], childScheduleRequests, parentNotificationWorkItems, subtreeCancellationPlans, valueSnapshots, cancellationToken);
            return;
        }

        ActivityExecutionState completedParentState;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> containerVariableSnapshots;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> completionDurableValueChanges;
        RuntimeStateChange<WorkflowExecutionState>? completionWorkflowVariableWriteBack = null;
        try
        {
            var completedAt = TimeProvider.GetUtcNow();
            var contract = parentExecutableNode.ActivityContract
                ?? throw new InvalidOperationException($"VF-ACT-001: Executable structural activity node '{parentExecutableNode.ExecutableNodeId}' has no pinned activity contract.");
            var openAttempt = currentParentState.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)
                ?? throw new InvalidOperationException($"VF-ACT-009: Running structural activity invocation '{currentParentState.InvocationId}' has no open committed attempt.");
            var completionTransition = completionCheckpointPreparation?.Transition
                                       ?? ActivityTransition.Complete(ActivityUnit.Value, resolvedContinuation.OutcomeName!);
            var completionProjection = await serviceProvider.GetRequiredService<ActivityCompletionProjector>().ProjectAsync(
                workItem.WorkflowExecutionId, currentParentState.InvocationId, openAttempt, contract, completionTransition, completedAt, cancellationToken);
            var captureProjection = await serviceProvider.GetRequiredService<RuntimeOutputCaptureProjector>().ProjectAsync(
                workItem.WorkflowExecutionId, currentParentState.InvocationId, parentExecutableNode,
                (IActivityCompletionTransition)completionTransition, completionProjection, completedAt, cancellationToken);
            completionDurableValueChanges = (completionCheckpointPreparation?.DurableValueChanges ?? [])
                .Concat(captureProjection.DurableValues)
                .GroupBy(change => change.StateId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            // A workflow-variable output capture writes the canonical root frame in the SAME commit as the
            // completion (#972), mirroring how the Set intrinsic commits its changed frame.
            completionWorkflowVariableWriteBack = await RuntimeWorkflowVariableCaptureWriteBack.BuildStateChangeAsync(
                serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>(),
                workItem.WorkflowExecutionId,
                parentExecutableNode.ExecutableNodeId,
                captureProjection.WorkflowVariableWrites,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                    [RuntimeMetadataKeys.CheckpointReason] = payload.Reason
                },
                cancellationToken);
            var completedAttempt = new ActivityAttempt(
                openAttempt.AttemptId, openAttempt.InvocationId, openAttempt.Ordinal, openAttempt.Reason,
                openAttempt.StartedAt, completedAt, openAttempt.TriggerDeliveryId,
                Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete);
            var priorAttempts = currentParentState.Attempts?.Where(attempt => attempt.AttemptId != openAttempt.AttemptId) ?? [];
            currentParentState = currentParentState with
            {
                Attempts = priorAttempts.Append(completedAttempt).OrderBy(attempt => attempt.Ordinal).ToArray(),
                Completion = completionProjection.Completion
            };
            var recordedOutputs = completionProjection.Projections
                .Where(item => item.Value.Presence != ValuePresence.Absent && item.Value.Policy.Storage != DurableValueStorage.External)
                .Select(item => new RecordedActivityOutput(item.Key, item.Value.Presence == ValuePresence.ExplicitNull ? null : item.Value.InlineValue))
                .ToArray();
            if (recordedOutputs.Length > 0)
            {
                valueSnapshots = valueSnapshots
                    .Concat(ActivityExecutionInspection.BuildOutputValueSnapshots(
                        payloadCapturePolicy, workItem, payload.ActivityExecutionId, payload.ExecutableNodeId,
                        parentExecutableNode.ActivityContract, recordedOutputs, RuntimeMetadataKeys.NotifyParentSchedulerWorkItemId, completedAt))
                    .ToArray();
            }

            containerVariableSnapshots = RuntimeContainerVariableEvidence.Capture(
                payloadCapturePolicy, scopeService, parentExecutableNode, currentParentState,
                workItem.WorkflowExecutionId, payload.ActivityExecutionId, workItem.WorkItemId, TimeProvider.GetUtcNow());
            completedParentState = CompleteParentActivity(workItem, payload, currentParentState, [completionProjection.Completion.OutcomeKey], completedAt);

            if (completedParentState.VariableFrame is not null || completedParentState.IterationVariableFrame is not null)
                completedParentState = RuntimeContainerScopeService.CloseOwnedFrames(completedParentState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordParentFaultAsync(
                activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter,
                workItem, payload, currentParentState, exception, "ParentNotificationFaulted", valueSnapshots, cancellationToken);
            return;
        }

        await StructuralParentEvaluationSupport.CommitCompletedParentActivityAsync(
            checkpointCommitter, inspectionAccumulator, TimeProvider, NewCommitSource(workItem, payload, payload.Reason), completedParentState,
            SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(completedParentState, skippedSubStatus: null), subtreeCancellationPlans, parentNotificationWorkItems,
            containerVariableSnapshots, completionDurableValueChanges, completionWorkflowVariableWriteBack, cancellationToken);
    }

    private async ValueTask RecordParentFaultAsync(
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeNotifyParentCommandPayload payload,
        ActivityExecutionState fallbackState,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var latestFaultedParentState = await activityExecutionStateStore.FindAsync(
            workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken) ?? fallbackState;
        var request = NewFaultIncidentRecordRequest(checkpointCommitter, workItem, payload, latestFaultedParentState, exception, subStatus, valueSnapshots);
        var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, payload.ActivityExecutionId, subStatus);
        var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
            activityExecutionStateStore, TimeProvider, workItem, payload.PinnedExecutable, latestFaultedParentState, incidentId, cancellationToken);
        await activityFaultIncidentRecorder.CommitAsync(
            parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
            cancellationToken);
    }

    private static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeNotifyParentCommandPayload payload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
            [RuntimeMetadataKeys.NotifyParentSchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.NotifyingChildActivityExecutionId] = payload.NotifyingChildActivityExecutionId,
            [RuntimeMetadataKeys.ParentNotificationCode] = payload.Code
        };

        return new ActivityFaultIncidentRecordRequest(
            CheckpointCommitter: checkpointCommitter,
            WorkItem: workItem,
            ActivityExecutionId: payload.ActivityExecutionId,
            ExecutableNodeId: payload.ExecutableNodeId,
            State: state,
            Exception: exception,
            SubStatus: subStatus,
            ActivityMetadata: activityMetadata,
            IncidentMetadata: new Dictionary<string, string>(activityMetadata, StringComparer.Ordinal),
            ValueSnapshots: valueSnapshots);
    }

    private static StructuralParentEvaluationSupport.ParentEvaluationCommitSource NewCommitSource(
        RuntimeSchedulerWorkItem notifyWorkItem,
        RuntimeNotifyParentCommandPayload notifyPayload,
        string checkpointReason) =>
        new(
            notifyWorkItem,
            notifyPayload.PinnedExecutable,
            notifyPayload.ExecutableNodeId,
            notifyPayload.ActivityExecutionId,
            Metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = notifyWorkItem.WorkItemId,
                [RuntimeMetadataKeys.CommandId] = notifyWorkItem.CommandId,
                [RuntimeMetadataKeys.CheckpointReason] = checkpointReason,
                [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
                [RuntimeMetadataKeys.ActivityExecutionId] = notifyPayload.ActivityExecutionId,
                [RuntimeMetadataKeys.ExecutableNodeId] = notifyPayload.ExecutableNodeId,
                [RuntimeMetadataKeys.NotifyingChildActivityExecutionId] = notifyPayload.NotifyingChildActivityExecutionId,
                [RuntimeMetadataKeys.ParentNotificationCode] = notifyPayload.Code,
                [RuntimeMetadataKeys.ExecutableArtifactId] = notifyPayload.PinnedExecutable.ArtifactId,
                [RuntimeMetadataKeys.ExecutableArtifactVersion] = notifyPayload.PinnedExecutable.ArtifactVersion,
                [RuntimeMetadataKeys.ExecutableArtifactHash] = notifyPayload.PinnedExecutable.ArtifactHash
            },
            DerivedCommandMetadata: notifyWorkItem.CommandMetadata);

    private ActivityExecutionState CompleteParentActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeNotifyParentCommandPayload payload,
        ActivityExecutionState state,
        IReadOnlyCollection<string> outcomeNames,
        DateTimeOffset completedAt)
    {
        var normalizedOutcomeNames = SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(outcomeNames, defaultToDone: true);
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.InvokeReason] = payload.Reason;
        metadata[RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(normalizedOutcomeNames);

        return RuntimeContainerScopeService.CloseOwnedFrames(state with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = completedAt,
            PrivateState = null,
            Metadata = metadata
        });
    }

    private static readonly string[] PayloadValidationParamNames =
    [
        "pinnedExecutable", "executableNodeId", "activityExecutionId", "parentActivityExecutionId", "branchId",
        "notifyingChildActivityExecutionId", "notifyingChildExecutableNodeId", "notifyingChildIterationId", "code", "reason"
    ];

    protected override RuntimeNotifyParentCommandPayload DeserializePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkItems.DeserializePayload<RuntimeNotifyParentCommandPayload>(workItem, "NotifyParentActivity", "notify parent payload", PayloadValidationParamNames);

}

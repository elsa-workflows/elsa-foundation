using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// The reactivation and commit shared by the structural evaluations that call a running structural activity outside a
/// child outcome: the child→parent notification (seam C) and the operator's intervention decision (spec 260 R-5). It
/// reactivates the target from its pinned snapshot, dispatches the evaluation's callback when the activity opts in, and
/// applies the returned continuation, with any child schedules, subtree cancellations and notifications it staged, in
/// one atomic checkpoint commit. A target that is no longer running acks the work item without a callback, and a target
/// that does not opt in acks it silently.
/// </summary>
internal static class StructuralCallbackEvaluation
{
    /// <summary>What one evaluation kind contributes to the shared machinery.</summary>
    internal sealed record Definition
    {
        /// <summary>The command name used in invariant messages, such as <c>NotifyParentActivity</c>.</summary>
        public required string CommandName { get; init; }

        /// <summary>The evaluation name used in invariant messages, such as <c>child-notification</c>.</summary>
        public required string EvaluationName { get; init; }

        /// <summary>The metadata key the evaluation's value snapshots record the work item under.</summary>
        public required string SnapshotWorkItemMetadataKey { get; init; }

        /// <summary>The checkpoint reason of a deferred or bypassed evaluation.</summary>
        public required string DeferredCheckpointReason { get; init; }

        /// <summary>The fault sub-status recorded when the evaluation throws.</summary>
        public required string FaultSubStatus { get; init; }

        /// <summary>The message of a cancellation whose activation disposal failed too.</summary>
        public required string CancellationDisposalMessage { get; init; }

        /// <summary>Whether the callback may stage ancestor notifications (with a <c>Complete</c> continuation only).</summary>
        public bool AllowsAncestorNotifications { get; init; }

        /// <summary>Whether the target opts into the callback; a target that does not acks the work item silently.</summary>
        public required Func<IActivity, bool> OptsIn { get; init; }

        /// <summary>Calls the target's callback.</summary>
        public required Func<IActivity, IRuntimeActivityExecutionContext, ValueTask<RuntimeStructuralContinuation>> InvokeAsync { get; init; }

        /// <summary>Builds the commit source of the evaluation's checkpoints.</summary>
        public required Func<string, StructuralParentEvaluationSupport.ParentEvaluationCommitSource> NewCommitSource { get; init; }

        /// <summary>Builds the fault-incident request when the evaluation faults the target.</summary>
        public required Func<RuntimeCheckpointCommitter, ActivityExecutionState, Exception, string, IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>, ActivityFaultIncidentRecordRequest> NewFaultIncidentRecordRequest { get; init; }

        /// <summary>Stamps the target state the callback's outcome commits, such as a processed marker; identity by default.</summary>
        public Func<ActivityExecutionState, ActivityExecutionState> MarkProcessed { get; init; } = static state => state;
    }

    /// <summary>
    /// Runs one evaluation for <paramref name="payload"/>'s target. Returns without a commit when the target is no
    /// longer running.
    /// </summary>
    public static async ValueTask RunAsync(
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        IActivityCommandPayload payload,
        Definition definition,
        Func<ActivityExecutionState, bool>? alreadyProcessed,
        CancellationToken cancellationToken)
    {
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();

        var executable = await PinnedExecutableRead.FindAsync(serviceProvider, payload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(payload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, payload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(payload.ExecutableNodeId, out var parentExecutableNode))
            throw new InvalidOperationException($"{definition.CommandName} scheduler work item '{workItem.WorkItemId}' references parent executable node '{payload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var parentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        if (parentState is null)
            throw new InvalidOperationException($"{definition.CommandName} scheduler work item '{workItem.WorkItemId}' references missing parent activity execution '{payload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(parentState.Execution.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"{definition.CommandName} scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but parent activity execution '{payload.ActivityExecutionId}' belongs to executable node '{parentState.Execution.ExecutableNodeId}'.");

        parentState.EnsureValueFlowCompatible();

        // A target that is no longer running acks the work item away (late-absorption, mirroring the completion path).
        if (parentState.Status != ActivityExecutionStatus.Running)
            return;

        // A repeated delivery the target already committed is acked by its processed marker.
        if (alreadyProcessed?.Invoke(parentState) == true)
            return;

        var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();
        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var checkpointCommitter = serviceProvider.GetService<RuntimeCheckpointCommitter>()
            ?? throw new InvalidOperationException($"A checkpoint committer is required to durably claim a structural {definition.EvaluationName} callback activation attempt.");
        var inspectionAccumulator = serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>();
        var activityFaultIncidentRecorder = serviceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var payloadCapturePolicy = serviceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();
        // Released once the outcome is recorded, after every fault boundary below has masked its text.
        using var faultMasking = ActivityFaultMasking.For(serviceProvider, payload.ActivityExecutionId);
        var scopeService = new RuntimeContainerScopeService(
            activityExecutionStateStore,
            serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>(),
            serviceProvider.GetRequiredService<IDurableValueStateStore>());

        SimpleActivityExecutionContext? context = null;
        RuntimeStructuralContinuation? continuation = null;
        IReadOnlyList<ActivityChildNotifiedContext> parentOutcomeNotifications = [];
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> subtreeCancellationPlans = [];
        IReadOnlyCollection<RuntimeSchedulerWorkItem> parentNotificationWorkItems = [];
        IReadOnlyCollection<RuntimeSchedulerWorkItem> ancestorNotificationWorkItems = [];
        ActivityExecutionState? callbackBypassParentState = null;
        ActivityActivationLease? activationLease = null;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots = [];
        RuntimeActivityCompletionCheckpointPreparation? completionCheckpointPreparation = null;
        try
        {
            var activationClaim = await ActivityAttemptActivationClaimer.ClaimStructuralCallbackAsync(
                checkpointCommitter,
                timeProvider,
                workItem,
                payload,
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
                definition.SnapshotWorkItemMetadataKey,
                timeProvider.GetUtcNow());
            var parentActivity = constructedParent.Activity;

            // The seam is opt-in by interface. A target that does not opt in acks the work item without invoking any
            // callback and without fault — end the attempt the claim opened and return the target to its prior deferred
            // state.
            if (!definition.OptsIn(parentActivity))
            {
                callbackBypassParentState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                    parentState,
                    Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend,
                    timeProvider.GetUtcNow());
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

                continuation = await definition.InvokeAsync(parentActivity, context);

                var scheduledChildren = context.GetChildActivityScheduleRequests();
                if (!continuation.IsDeferred && scheduledChildren.Count > 0)
                    throw new InvalidOperationException($"A terminal structural decision cannot also schedule child activities in the same {definition.EvaluationName} evaluation.");
                RuntimeDelayedSchedules.Validate(serviceProvider, timeProvider, payload.ExecutableNodeId, scheduledChildren);

                // Seam-B fault absorption and intervention stay child-fault-evaluation-only powers.
                if (context.GetChildFaultAbsorptionRequests().Count > 0)
                    throw new InvalidOperationException("A child-fault absorption is only valid in a child-fault evaluation.");
                if (context.GetChildFaultInterventionRequests().Count > 0)
                    throw new InvalidOperationException("A child-fault intervention is only valid in a child-fault evaluation.");

                // An ancestor notification is otherwise an initial-execution and child-completion power.
                var ancestorRequests = context.GetAncestorNotificationRequests();
                if (ancestorRequests.Count > 0 && !definition.AllowsAncestorNotifications)
                    throw new InvalidOperationException("An ancestor notification is only valid in an initial structural execution.");

                subtreeCancellationPlans = await StructuralParentEvaluationSupport.PlanChildSubtreeCancellationsAsync(
                    serviceProvider,
                    activityExecutionStateStore,
                    timeProvider,
                    workItem,
                    payload.ActivityExecutionId,
                    context.GetChildSubtreeCancellationRequests(),
                    continuation,
                    cancellationToken);

                // As in a child-completion evaluation, an ancestor notification rides only a Complete continuation; no
                // matching ancestor faults the target with a stable code.
                if (ancestorRequests.Count > 0)
                {
                    try
                    {
                        ancestorNotificationWorkItems = await ParentNotificationEvaluation.BuildAncestorAsync(
                            activityExecutionStateStore,
                            timeProvider,
                            workItem,
                            executable,
                            parentState,
                            ancestorRequests,
                            continuation,
                            cancellationToken);
                    }
                    catch (AncestorNotificationTargetNotFoundException targetNotFound)
                    {
                        continuation = RuntimeStructuralContinuation.Faulted(
                            new ActivityFault(AncestorNotificationTargetNotFoundException.FaultCode, targetNotFound.Message));
                    }
                }

                // The consumer may itself notify ITS parent (bubbling is the consumer's recursion).
                var parentNotifications = await ParentNotificationEvaluation.BuildAsync(
                    activityExecutionStateStore,
                    timeProvider,
                    workItem,
                    payload.PinnedExecutable,
                    parentState,
                    context.GetParentNotificationRequests(),
                    continuation,
                    cancellationToken);
                parentNotificationWorkItems = parentNotifications.WorkItems;
                parentOutcomeNotifications = parentNotifications.FaultOutcomeNotifications;

                if (continuation.IsComplete && parentActivity is IRuntimeActivityCheckpointParticipant checkpointParticipant)
                {
                    var persistedValues = await durableValueStateStore.ListAllDurableValueStatesAsync(workItem.WorkflowExecutionId, cancellationToken);
                    completionCheckpointPreparation = await checkpointParticipant.PrepareCompletionCheckpointAsync(
                        context,
                        persistedValues,
                        timeProvider.GetUtcNow(),
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
            if (await ActivityActivationLeaseDisposer.DisposeAfterCancellationAsync(activationLease, cancellationException, definition.CancellationDisposalMessage, faultMasking.Mask) is { } cleanupFailure)
                throw cleanupFailure;
            throw;
        }
        catch (Exception exception)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            var fault = disposalException is null ? exception : ActivityActivationLeaseDisposer.CombineExecutionFailure(exception, disposalException);
            var subStatus = disposalException is null ? definition.FaultSubStatus : "ActivityDisposalFailed";
            await RecordParentFaultAsync(
                activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, faultMasking, timeProvider,
                workItem, payload, definition, parentState, fault, subStatus, valueSnapshots, cancellationToken);
            return;
        }

        var activationDisposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
        activationLease = null;
        if (activationDisposalException is not null)
        {
            await RecordParentFaultAsync(
                activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, faultMasking, timeProvider,
                workItem, payload, definition, parentState, activationDisposalException, "ActivityDisposalFailed", valueSnapshots, cancellationToken);
            return;
        }

        if (callbackBypassParentState is not null)
        {
            await StructuralParentEvaluationSupport.CommitDeferredParentActivityAsync(
                checkpointCommitter, inspectionAccumulator, idGenerator, timeProvider, definition.NewCommitSource(definition.DeferredCheckpointReason),
                callbackBypassParentState, [], [], [], subtreeCancellationPlans, valueSnapshots, cancellationToken);
            return;
        }

        var resolvedContext = context ?? throw new InvalidOperationException($"Structural {definition.EvaluationName} callback did not create an execution context.");
        var resolvedContinuation = continuation ?? throw new InvalidOperationException($"Structural {definition.EvaluationName} callback did not return a continuation.");
        var currentParentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken) ?? parentState;
        if (currentParentState.Status != ActivityExecutionStatus.Running)
            return;

        currentParentState = definition.MarkProcessed(RuntimeStructuralStateProjector.Apply(currentParentState, resolvedContinuation, timeProvider.GetUtcNow()));

        if (resolvedContinuation.Kind == RuntimeStructuralContinuationKind.Fault)
        {
            var fault = faultMasking.Mask(resolvedContinuation.Fault!);
            var faultedParentState = currentParentState with
            {
                Fault = fault.ToNormalized()
            };
            var exception = new ActivityTransitionFaultException(fault);
            var request = definition.NewFaultIncidentRecordRequest(checkpointCommitter, faultedParentState, exception, "ActivityReturnedFault", valueSnapshots);
            var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, payload.ActivityExecutionId, "ActivityReturnedFault");
            var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
                activityExecutionStateStore,
                timeProvider,
                workItem,
                payload.PinnedExecutable,
                faultedParentState,
                incidentId,
                cancellationToken,
                fault,
                parentOutcomeNotifications);
            if (parentOutcomeNotifications.Count > 0 && parentEvaluation is not null)
                request = ChildFaultParentEvaluation.MarkPendingOutcome(request, parentEvaluation);
            await activityFaultIncidentRecorder.CommitAsync(
                parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
                cancellationToken);
            return;
        }

        if (resolvedContinuation.Kind == RuntimeStructuralContinuationKind.Cancel)
        {
            await ActivityCancellationCheckpointService.CommitAsync(
                checkpointCommitter, inspectionAccumulator, timeProvider, workItem,
                currentParentState, resolvedContinuation.CancellationReason!, valueSnapshots, cancellationToken: cancellationToken);
            return;
        }

        var childScheduleRequests = resolvedContext.GetChildActivityScheduleRequests();
        if (resolvedContinuation.IsDeferred)
        {
            currentParentState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                currentParentState, Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, timeProvider.GetUtcNow());
            await StructuralParentEvaluationSupport.CommitDeferredParentActivityAsync(
                checkpointCommitter, inspectionAccumulator, idGenerator, timeProvider, definition.NewCommitSource(definition.DeferredCheckpointReason),
                currentParentState, [], childScheduleRequests, parentNotificationWorkItems, subtreeCancellationPlans, valueSnapshots, cancellationToken);
            return;
        }

        ActivityExecutionState completedParentState;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> containerVariableSnapshots;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> completionDurableValueChanges;
        RuntimeStateChange<WorkflowExecutionState>? completionWorkflowVariableWriteBack = null;
        try
        {
            var completedAt = timeProvider.GetUtcNow();
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
                (IActivityCompletionTransition)completionTransition, completionProjection, completedAt, cancellationToken,
                // A capture into a graph variable writes the row of the producer's nearest closed scope.
                closedScopeBoundary: await ClosedScopeCaptureBoundary.FindAsync(serviceProvider, workItem.WorkflowExecutionId, currentParentState.InvocationId, parentExecutableNode, cancellationToken));
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
                        parentExecutableNode.ActivityContract, recordedOutputs, definition.SnapshotWorkItemMetadataKey, completedAt))
                    .ToArray();
            }

            containerVariableSnapshots = RuntimeContainerVariableEvidence.Capture(
                payloadCapturePolicy, scopeService, parentExecutableNode, currentParentState,
                workItem.WorkflowExecutionId, payload.ActivityExecutionId, workItem.WorkItemId, timeProvider.GetUtcNow());
            completedParentState = StructuralParentEvaluationSupport.CompleteParentActivity(workItem, payload, currentParentState, [completionProjection.Completion.OutcomeKey], completedAt);

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
                activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, faultMasking, timeProvider,
                workItem, payload, definition, currentParentState, exception, definition.FaultSubStatus, valueSnapshots, cancellationToken);
            return;
        }

        var completedValueSnapshots = valueSnapshots.Concat(containerVariableSnapshots).ToArray();
        await StructuralParentEvaluationSupport.CommitCompletedParentActivityAsync(
            checkpointCommitter, inspectionAccumulator, timeProvider, definition.NewCommitSource(payload.Reason), completedParentState,
            SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(completedParentState, skippedSubStatus: null), subtreeCancellationPlans, parentNotificationWorkItems,
            completedValueSnapshots, completionDurableValueChanges, completionWorkflowVariableWriteBack, cancellationToken,
            ancestorNotifications: ancestorNotificationWorkItems);
    }

    private static async ValueTask RecordParentFaultAsync(
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        ActivityFaultMasking faultMasking,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        IActivityCommandPayload payload,
        Definition definition,
        ActivityExecutionState fallbackState,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var latestFaultedParentState = await activityExecutionStateStore.FindAsync(
            workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken) ?? fallbackState;
        // Every arm that records a thrown exception records through here, so this is where a value resolved for the
        // parent leaves that exception's text. The returned-fault arm masks its fault once and builds its request from
        // that masked fault directly, without coming through here.
        var request = definition.NewFaultIncidentRecordRequest(checkpointCommitter, latestFaultedParentState, faultMasking.Mask(exception), subStatus, valueSnapshots);
        var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, payload.ActivityExecutionId, subStatus);
        var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
            activityExecutionStateStore, timeProvider, workItem, payload.PinnedExecutable, latestFaultedParentState, incidentId, cancellationToken);
        await activityFaultIncidentRecorder.CommitAsync(
            parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
            cancellationToken);
    }
}

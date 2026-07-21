using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

public sealed class WorkflowParentActivityCompletionSchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    // W14 (NM naming pass): deliberately NOT renamed. HandlerName is a persisted handler identifier —
    // nameof(...) is written verbatim into scheduler poison/drain records and matched on recovery, so renaming
    // the type would change a wire value. Keep the type name to preserve the persisted HandlerName.
    public const string HandlerName = nameof(WorkflowParentActivityCompletionSchedulerWorkHandler);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the handler. RT-8: collapsed to a single primary constructor (the former ambient-services-accessor
    /// overload is gone — RT-7 replaced that AsyncLocal service locator with the explicit
    /// <see cref="IRuntimePipelineContext"/> workspace carrier).
    /// </summary>
    public WorkflowParentActivityCompletionSchedulerWorkHandler(
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _serviceScopeFactory = serviceScopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (workItem.CommandKind != WorkflowExecutionCommandKind.CompleteActivity)
            return false;

        if (workItem.Payload is null)
            return false;

        try
        {
            // RT-11: reuse the single per-work-item parse rather than deserializing the payload again.
            var completionPayload = RuntimeCompleteActivityPayloadMemo.Deserialize(workItem);
            return completionPayload?.CompletionKind == SchedulerCompletionKind.ParentCompletionEvaluation;
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsCompletePayloadValidationException(argumentException))
        {
            return false;
        }
    }

    /// <summary>
    /// Direct (no-pipeline) dispatch: runs against a fresh scope. RT-7: the former AsyncLocal ambient-services read is
    /// gone; the pipeline overload carries the drain's services explicitly on the workspace.
    /// </summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        await ExecuteAsync(workItem, ambientServices: null, cancellationToken);
    }

    /// <summary>
    /// Pipeline dispatch (Move 2 / RT-7): run in the Invoke slot reading the drain's ambient services from the workspace
    /// (staged explicitly by the dispatcher) instead of an AsyncLocal service locator.
    /// </summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(pipelineContext);
        cancellationToken.ThrowIfCancellationRequested();

        await ExecuteAsync(workItem, pipelineContext.Workspace.AmbientServices, cancellationToken);
    }

    private async ValueTask ExecuteAsync(RuntimeSchedulerWorkItem workItem, IServiceProvider? ambientServices, CancellationToken cancellationToken)
    {
        var payload = DeserializeCompletePayload(workItem);
        if (payload.CompletionKind != SchedulerCompletionKind.ParentCompletionEvaluation)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' is not parent completion evaluation work.");

        if (ambientServices is { } provider)
        {
            await HandleWithServicesAsync(workItem, payload, provider, cancellationToken);
            return;
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        await HandleWithServicesAsync(workItem, payload, scope.ServiceProvider, cancellationToken);
    }

    private async ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();

        // spec 111: burst-cached pinned-executable read.
        var executable = await PinnedExecutableRead.FindAsync(serviceProvider, payload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(payload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, payload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(payload.ExecutableNodeId, out var parentExecutableNode))
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references parent executable node '{payload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var parentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        if (parentState is null)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references missing parent activity execution '{payload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (payload.CompletedChildActivityExecutionId is not { } completedChildActivityExecutionId)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' requires a completed child activity execution ID.");

        var completedChildState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, completedChildActivityExecutionId, cancellationToken);
        if (completedChildState is null)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references missing completed child activity execution '{completedChildActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!executable.NodesById.ContainsKey(completedChildState.Execution.ExecutableNodeId))
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references completed child executable node '{completedChildState.Execution.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        if (!StringComparer.Ordinal.Equals(parentState.Execution.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but parent activity execution '{payload.ActivityExecutionId}' belongs to executable node '{parentState.Execution.ExecutableNodeId}'.");

        parentState.EnsureValueFlowCompatible();

        if (parentState.Status != ActivityExecutionStatus.Running)
            return;

        if (ActivityAttemptActivationClaimer.WasParentCompletionProcessed(completedChildState))
            return;

        // spec 112 FR-8: a completion evaluation for a child this parent already cancelled is late by
        // definition — ack it without invoking the structural callback.
        if (completedChildState.Status == ActivityExecutionStatus.Cancelled)
            return;

        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();
        var checkpointCommitter = serviceProvider.GetService<RuntimeCheckpointCommitter>();
        var inspectionAccumulator = serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>();
        var activityFaultIncidentRecorder = serviceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var payloadCapturePolicy = serviceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();

        // One scope service serves both the self-owner scope built for the child-completion evaluation below
        // and the completed-scope evidence capture on the completion path (ADR 0027/0030).
        var scopeService = new RuntimeContainerScopeService(
            activityExecutionStateStore,
            serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>());

        SimpleActivityExecutionContext? context = null;
        RuntimeStructuralContinuation? continuation = null;
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> subtreeCancellationPlans = [];
        ActivityExecutionState? callbackBypassParentState = null;
        IReadOnlyCollection<RuntimeSchedulerWorkItem> callbackBypassContinuationWorkItems = [];
        ActivityActivationLease? activationLease = null;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots = [];
        RuntimeActivityCompletionCheckpointPreparation? completionCheckpointPreparation = null;
        try
        {
            if (checkpointCommitter is null)
                throw new InvalidOperationException("A checkpoint committer is required to durably claim a structural callback activation attempt.");

            var activationClaim = await ActivityAttemptActivationClaimer.ClaimStructuralCallbackAsync(
                checkpointCommitter,
                _timeProvider,
                workItem,
                payload,
                parentState,
                parentExecutableNode.ActivityContract!.SideEffectProfile,
                cancellationToken);
            parentState = activationClaim.State;
            var constructedParent = await ConstructActivityAsync(
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
                RuntimeMetadataKeys.ParentCompletionSchedulerWorkItemId,
                _timeProvider.GetUtcNow());
            var parentActivity = constructedParent.Activity;
            var childFaulted = IsChildFaulted(workItem);

            if (childFaulted)
            {
                // A faulted child is propagated only to parents that opt into child-fault handling (fork/join
                // composites). For any other parent the fault stays a blocking incident — sequential containers
                // must halt on a faulted step, not advance past it.
                if (parentActivity is not IRuntimeActivityChildFaultHandler)
                {
                    callbackBypassParentState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                        parentState,
                        Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend,
                        _timeProvider.GetUtcNow());
                }
            }
            else if (parentActivity is not IRuntimeActivityChildCompletionHandler)
            {
                callbackBypassParentState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                    parentState,
                    Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend,
                    _timeProvider.GetUtcNow());
                callbackBypassContinuationWorkItems = [NewContinuationSchedulingWorkItem(workItem, payload)];
            }

            if (callbackBypassParentState is null)
            {
                // spec 119 D4: a parent that races sibling children (the BPMN event-based gateway) needs its
                // direct, non-terminal children's activity-execution ids to stage seam-A subtree cancellations.
                // This parent-scoped read is opt-in (marker-gated), so non-consumer structural activities pay nothing.
                var liveChildActivities = parentActivity is IRuntimeLiveChildActivityConsumer
                    ? await LoadLiveChildActivitiesAsync(activityExecutionStateStore, workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken)
                    : [];

                context = SimpleActivityExecutionContext.ForExecution(
                    parentActivity,
                    cancellationToken,
                    workItem.WorkflowExecutionId,
                    payload.PinnedExecutable,
                    workItem,
                    parentExecutableNode,
                    parentState,
                    variableScope: null,
                    liveChildActivities: liveChildActivities);

                if (childFaulted)
                {
                    var childFaultedContext = new ActivityChildFaultedContext(
                        context,
                        completedChildActivityExecutionId,
                        completedChildState.Execution.ExecutableNodeId,
                        ReadIncidentId(workItem),
                        completedChildState.IterationId);

                    continuation = await ((IRuntimeActivityChildFaultHandler)parentActivity).OnChildFaultedAsync(childFaultedContext);
                }
                else
                {
                    var childCompletedContext = new ActivityChildCompletedContext(
                        context,
                        completedChildActivityExecutionId,
                        completedChildState.Execution.ExecutableNodeId,
                        payload.OutcomeNames,
                        completedChildState.IterationId);

                    continuation = await ((IRuntimeActivityChildCompletionHandler)parentActivity).OnChildCompletedAsync(childCompletedContext);
                }

                var scheduledChildren = context.GetChildActivityScheduleRequests();
                if (!continuation.IsDeferred && scheduledChildren.Count > 0)
                    throw new InvalidOperationException("A terminal structural decision cannot also schedule child activities in the same child-completion evaluation.");

                subtreeCancellationPlans = await PlanChildSubtreeCancellationsAsync(
                    serviceProvider,
                    activityExecutionStateStore,
                    workItem,
                    payload,
                    context.GetChildSubtreeCancellationRequests(),
                    continuation,
                    cancellationToken);

                var absorptionPlan = await PlanChildFaultAbsorptionAsync(
                    serviceProvider,
                    activityExecutionStateStore,
                    workItem,
                    payload,
                    context.GetChildFaultAbsorptionRequests(),
                    continuation,
                    childFaulted,
                    completedChildState,
                    cancellationToken);
                if (absorptionPlan is not null)
                    subtreeCancellationPlans = [.. subtreeCancellationPlans, absorptionPlan];

                if (continuation.IsComplete && parentActivity is IRuntimeActivityCheckpointParticipant checkpointParticipant)
                {
                    var persistedValues = await durableValueStateStore.ListAllDurableValueStatesAsync(workItem.WorkflowExecutionId, cancellationToken);
                    completionCheckpointPreparation = await checkpointParticipant.PrepareCompletionCheckpointAsync(
                        context,
                        persistedValues,
                        _timeProvider.GetUtcNow(),
                        cancellationToken);
                    var completionTransition = (IActivityCompletionTransition)completionCheckpointPreparation.Transition;
                    if (!StringComparer.Ordinal.Equals(completionTransition.Outcome, continuation.OutcomeName))
                    {
                        throw new InvalidOperationException(
                            $"Checkpoint participant completion outcome '{completionTransition.Outcome}' does not match structural continuation outcome '{continuation.OutcomeName}'.");
                    }
                }
            }

        }
        catch (OperationCanceledException cancellationException) when (cancellationToken.IsCancellationRequested)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            if (disposalException is not null)
                throw new AggregateException("Structural callback cancellation and activation disposal both failed.", cancellationException, disposalException);
            throw;
        }
        catch (Exception exception)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            var fault = disposalException is null
                ? exception
                : ActivityActivationLeaseDisposer.Combine(exception, disposalException);
            var subStatus = disposalException is null ? "ParentCompletionFaulted" : "ActivityDisposalFailed";
            if (checkpointCommitter is null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
                throw;
            }
            await RecordParentFaultAsync(
                activityFaultIncidentRecorder,
                activityExecutionStateStore,
                checkpointCommitter,
                workItem,
                payload,
                parentState,
                fault,
                subStatus,
                valueSnapshots,
                cancellationToken);
            return;
        }

        var activationDisposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
        activationLease = null;
        if (activationDisposalException is not null)
        {
            await RecordParentFaultAsync(
                activityFaultIncidentRecorder,
                activityExecutionStateStore,
                checkpointCommitter!,
                workItem,
                payload,
                parentState,
                activationDisposalException,
                "ActivityDisposalFailed",
                valueSnapshots,
                cancellationToken);
            return;
        }

        if (callbackBypassParentState is not null)
        {
            await CommitDeferredParentActivityAsync(
                checkpointCommitter!,
                inspectionAccumulator,
                idGenerator,
                workItem,
                payload,
                callbackBypassParentState,
                ActivityAttemptActivationClaimer.MarkParentCompletionProcessed(completedChildState, workItem.WorkItemId),
                [],
                callbackBypassContinuationWorkItems,
                [],
                valueSnapshots,
                cancellationToken);
            return;
        }

        var resolvedContext = context
            ?? throw new InvalidOperationException("Structural child callback did not create an execution context.");
        var resolvedContinuation = continuation
            ?? throw new InvalidOperationException("Structural child callback did not return a continuation.");
        var currentParentState = await activityExecutionStateStore.FindAsync(
                                     workItem.WorkflowExecutionId,
                                     payload.ActivityExecutionId,
                                     cancellationToken)
                                 ?? parentState;
        if (currentParentState.Status != ActivityExecutionStatus.Running)
            return;

        currentParentState = RuntimeStructuralStateProjector.Apply(currentParentState, resolvedContinuation, _timeProvider.GetUtcNow());

        if (resolvedContinuation.Kind == RuntimeStructuralContinuationKind.Fault)
        {
            if (checkpointCommitter is null)
                throw new InvalidOperationException("A checkpoint committer is required to persist a structural activity fault.");

            var fault = resolvedContinuation.Fault!;
            var faultedParentState = currentParentState with
            {
                Fault = new NormalizedActivityFault(
                    fault.Code,
                    typeof(ActivityFault).FullName!,
                    fault.Message,
                    sanitizedStackTrace: null,
                    fault.IsRetryable)
            };
            var exception = new ActivityTransitionFaultException(fault);
            var request = NewFaultIncidentRecordRequest(
                checkpointCommitter,
                workItem,
                payload,
                faultedParentState,
                exception,
                "ActivityReturnedFault",
                valueSnapshots);
            var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, payload.ActivityExecutionId, "ActivityReturnedFault");
            var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
                activityExecutionStateStore,
                _timeProvider,
                workItem,
                payload.PinnedExecutable,
                faultedParentState,
                incidentId,
                cancellationToken);
            await activityFaultIncidentRecorder.CommitAsync(
                parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
                cancellationToken);
            return;
        }

        if (resolvedContinuation.Kind == RuntimeStructuralContinuationKind.Cancel)
        {
            if (checkpointCommitter is null)
                throw new InvalidOperationException("A checkpoint committer is required to persist a structural activity cancellation.");

            await ActivityCancellationCheckpointService.CommitAsync(
                checkpointCommitter,
                inspectionAccumulator,
                _timeProvider,
                workItem,
                currentParentState,
                resolvedContinuation.CancellationReason!,
                valueSnapshots,
                cancellationToken: cancellationToken);
            return;
        }

        var childScheduleRequests = resolvedContext.GetChildActivityScheduleRequests();
        if (resolvedContinuation.IsDeferred)
        {
            currentParentState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                currentParentState,
                Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend,
                _timeProvider.GetUtcNow());
            await CommitDeferredParentActivityAsync(
                checkpointCommitter!,
                inspectionAccumulator,
                idGenerator,
                workItem,
                payload,
                currentParentState,
                ActivityAttemptActivationClaimer.MarkParentCompletionProcessed(completedChildState, workItem.WorkItemId),
                childScheduleRequests,
                [],
                subtreeCancellationPlans,
                valueSnapshots,
                cancellationToken);
            return;
        }

        ActivityExecutionState completedParentState;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> containerVariableSnapshots;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> completionDurableValueChanges;
        try
        {
            var completedAt = _timeProvider.GetUtcNow();
            var contract = parentExecutableNode.ActivityContract
                ?? throw new InvalidOperationException($"VF-ACT-001: Executable structural activity node '{parentExecutableNode.ExecutableNodeId}' has no pinned activity contract.");
            var openAttempt = currentParentState.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)
                ?? throw new InvalidOperationException($"VF-ACT-009: Running structural activity invocation '{currentParentState.InvocationId}' has no open committed attempt.");
            var completionTransition = completionCheckpointPreparation?.Transition
                                       ?? ActivityTransition.Complete(ActivityUnit.Value, resolvedContinuation.OutcomeName!);
            var completionProjection = await serviceProvider.GetRequiredService<ActivityCompletionProjector>().ProjectAsync(
                workItem.WorkflowExecutionId,
                currentParentState.InvocationId,
                openAttempt,
                contract,
                completionTransition,
                completedAt,
                cancellationToken);
            var outputCaptureChanges = await serviceProvider.GetRequiredService<RuntimeOutputCaptureProjector>().ProjectAsync(
                workItem.WorkflowExecutionId,
                currentParentState.InvocationId,
                parentExecutableNode,
                (IActivityCompletionTransition)completionTransition,
                completionProjection,
                completedAt,
                cancellationToken);
            completionDurableValueChanges = (completionCheckpointPreparation?.DurableValueChanges ?? [])
                .Concat(outputCaptureChanges)
                .GroupBy(change => change.StateId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var completedAttempt = new ActivityAttempt(
                openAttempt.AttemptId,
                openAttempt.InvocationId,
                openAttempt.Ordinal,
                openAttempt.Reason,
                openAttempt.StartedAt,
                completedAt,
                openAttempt.TriggerDeliveryId,
                Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete);
            var priorAttempts = currentParentState.Attempts?.Where(attempt => attempt.AttemptId != openAttempt.AttemptId) ?? [];
            currentParentState = currentParentState with
            {
                Attempts = priorAttempts.Append(completedAttempt).OrderBy(attempt => attempt.Ordinal).ToArray(),
                Completion = completionProjection.Completion
            };
            var recordedOutputs = completionProjection.Projections
                .Where(item => item.Value.Presence != ValuePresence.Absent && item.Value.Policy.Storage != DurableValueStorage.External)
                .Select(item => new RecordedActivityOutput(
                    item.Key,
                    item.Value.Presence == ValuePresence.ExplicitNull ? null : item.Value.InlineValue))
                .ToArray();
            if (recordedOutputs.Length > 0)
            {
                valueSnapshots = valueSnapshots
                    .Concat(ActivityExecutionInspection.BuildOutputValueSnapshots(
                        payloadCapturePolicy,
                        workItem,
                        payload.ActivityExecutionId,
                        payload.ExecutableNodeId,
                        parentExecutableNode.ActivityContract,
                        recordedOutputs,
                        RuntimeMetadataKeys.ParentCompletionSchedulerWorkItemId,
                        completedAt))
                    .ToArray();
            }

            // A completing container's scope is no longer live for runtime expressions; mark it completed
            // and retain its final variable values as inspection evidence only through the configured
            // capture/retention policy (ADR 0027, #210).
            containerVariableSnapshots = RuntimeContainerVariableEvidence.Capture(
                payloadCapturePolicy, scopeService, parentExecutableNode, currentParentState,
                workItem.WorkflowExecutionId, payload.ActivityExecutionId, workItem.WorkItemId, _timeProvider.GetUtcNow());
            completedParentState = CompleteParentActivity(
                workItem,
                payload,
                currentParentState,
                [completionProjection.Completion.OutcomeKey],
                completedAt);

            // Only a container that actually owns scoped variables gets its scope marked completed; this
            // keeps the completion of ordinary containers untouched (ADR 0027, #210).
            if (completedParentState.VariableFrame is not null || completedParentState.IterationVariableFrame is not null)
                completedParentState = RuntimeContainerScopeService.CloseOwnedFrames(completedParentState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (checkpointCommitter is null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
                throw;
            }

            await RecordParentFaultAsync(
                activityFaultIncidentRecorder,
                activityExecutionStateStore,
                checkpointCommitter,
                workItem,
                payload,
                currentParentState,
                exception,
                "ParentCompletionFaulted",
                valueSnapshots,
                cancellationToken);
            return;
        }

        if (checkpointCommitter is null)
            throw new InvalidOperationException("A checkpoint committer is required to atomically persist structural activity completion.");

        await CommitCompletedParentActivityAsync(
            checkpointCommitter,
            inspectionAccumulator,
            workItem,
            payload,
            completedParentState,
            ReadCompletionOutcomeNames(completedParentState),
            subtreeCancellationPlans,
            containerVariableSnapshots,
            completionDurableValueChanges,
            cancellationToken);
    }

    /// <summary>
    /// Loads the parent's direct, non-terminal child executions (spec 119 D4) for an opt-in
    /// <c>IRuntimeLiveChildActivityConsumer</c> parent, so its child-completion/child-fault callback can
    /// resolve a losing sibling's node id to its live activity-execution id. Terminal children are excluded —
    /// they are never cancellation targets (the planner skips them) and a losing sibling that already
    /// terminalized is a benign seam-112 no-op.
    /// </summary>
    private static async ValueTask<IReadOnlyCollection<RuntimeLiveChildActivity>> LoadLiveChildActivitiesAsync(
        IActivityExecutionStateStore activityExecutionStateStore,
        string workflowExecutionId,
        string parentActivityExecutionId,
        CancellationToken cancellationToken)
    {
        var children = await activityExecutionStateStore.ListAllByParentAsync(workflowExecutionId, parentActivityExecutionId, cancellationToken);
        return children
            .Where(child => child.Status is not (
                ActivityExecutionStatus.Completed or
                ActivityExecutionStatus.Faulted or
                ActivityExecutionStatus.Cancelled or
                ActivityExecutionStatus.Recovered))
            .Select(child => new RuntimeLiveChildActivity(child.Execution.ActivityExecutionId, child.Execution.ExecutableNodeId, child.Status))
            .ToArray();
    }

    /// <summary>
    /// Validates and plans the child-subtree cancellations the structural callback staged (spec 112).
    /// Structural misuse (unknown target, non-child target, duplicate, terminal continuation) faults the
    /// evaluation; a target that is already terminal is a legal first-completion-wins race and is skipped.
    /// </summary>
    private async ValueTask<IReadOnlyCollection<ActivitySubtreeCancellationPlan>> PlanChildSubtreeCancellationsAsync(
        IServiceProvider serviceProvider,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        IReadOnlyCollection<RuntimeChildSubtreeCancellationRequest> requests,
        RuntimeStructuralContinuation continuation,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return [];

        if (continuation.Kind is RuntimeStructuralContinuationKind.Fault or RuntimeStructuralContinuationKind.Cancel)
            throw new InvalidOperationException("A faulting or cancelling structural decision cannot also cancel child subtrees in the same child-completion evaluation.");

        var duplicateTarget = requests.GroupBy(request => request.ChildActivityExecutionId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTarget is not null)
            throw new InvalidOperationException($"Child subtree cancellation targets activity execution '{duplicateTarget.Key}' more than once.");

        var planner = serviceProvider.GetRequiredService<ActivitySubtreeCancellationPlanner>();
        var allStates = await activityExecutionStateStore.ListAllAsync(workItem.WorkflowExecutionId, cancellationToken);
        var byId = allStates.ToDictionary(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal);
        var occurredAt = _timeProvider.GetUtcNow();
        var plans = new List<ActivitySubtreeCancellationPlan>(requests.Count);
        foreach (var request in requests)
        {
            if (!byId.TryGetValue(request.ChildActivityExecutionId, out var target))
                throw new InvalidOperationException($"Child subtree cancellation references missing activity execution '{request.ChildActivityExecutionId}'.");
            if (!StringComparer.Ordinal.Equals(target.ParentActivityExecutionId, payload.ActivityExecutionId))
                throw new InvalidOperationException($"Child subtree cancellation targets activity execution '{request.ChildActivityExecutionId}', which is not a child of parent activity execution '{payload.ActivityExecutionId}'.");
            if (target.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
                continue;

            plans.Add(await planner.PlanAsync(
                workItem.WorkflowExecutionId,
                target,
                allStates,
                subStatus: "ParentCancelled",
                NewStagedCancellationMetadata(workItem, request.Reason, payload.ActivityExecutionId, request.Metadata),
                occurredAt,
                cancellationToken));
        }

        return plans;
    }

    /// <summary>
    /// Validates and plans the child-fault absorption the structural callback staged (spec 115):
    /// the evaluation's incident resolves and the faulted child's leftover subtree is reclaimed,
    /// riding the same commit as the continuation. Returns <see langword="null"/> when nothing was
    /// staged; an already-terminal incident is a legal redelivery race and only skips the resolution.
    /// </summary>
    private async ValueTask<ActivitySubtreeCancellationPlan?> PlanChildFaultAbsorptionAsync(
        IServiceProvider serviceProvider,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        IReadOnlyCollection<RuntimeChildFaultAbsorptionRequest> requests,
        RuntimeStructuralContinuation continuation,
        bool childFaulted,
        ActivityExecutionState faultedChildState,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return null;

        if (!childFaulted)
            throw new InvalidOperationException("A child-fault absorption is only valid in a child-fault evaluation.");
        if (requests.Count > 1)
            throw new InvalidOperationException("A child-fault evaluation cannot stage a fault absorption more than once.");
        if (continuation.Kind is RuntimeStructuralContinuationKind.Fault or RuntimeStructuralContinuationKind.Cancel)
            throw new InvalidOperationException("A faulting or cancelling structural decision cannot also absorb the child fault.");

        var request = requests.Single();
        var evaluationIncidentId = ReadIncidentId(workItem);
        if (evaluationIncidentId is null || !StringComparer.Ordinal.Equals(request.IncidentId, evaluationIncidentId))
            throw new InvalidOperationException($"Child-fault absorption names incident '{request.IncidentId}', but this evaluation carries incident '{evaluationIncidentId ?? "<none>"}'.");

        var incidentStateStore = serviceProvider.GetRequiredService<IIncidentStateStore>();
        var incident = await incidentStateStore.FindAsync(workItem.WorkflowExecutionId, request.IncidentId, cancellationToken)
            ?? throw new InvalidOperationException($"Child-fault absorption references missing incident '{request.IncidentId}'.");

        var occurredAt = _timeProvider.GetUtcNow();
        var planner = serviceProvider.GetRequiredService<ActivitySubtreeCancellationPlanner>();
        var allStates = await activityExecutionStateStore.ListAllAsync(workItem.WorkflowExecutionId, cancellationToken);
        var plan = await planner.PlanAsync(
            workItem.WorkflowExecutionId,
            faultedChildState,
            allStates,
            subStatus: "FaultAbsorbed",
            NewStagedCancellationMetadata(workItem, request.Reason, payload.ActivityExecutionId, request.Metadata),
            occurredAt,
            cancellationToken);

        // The absorbed incident resolves instead of being suppressed with the rest of the subtree.
        var incidentChanges = plan.IncidentChanges
            .Where(item => !StringComparer.Ordinal.Equals(item.IncidentId, incident.IncidentId))
            .ToList();
        if (incident.Status is not (IncidentStatus.Resolved or IncidentStatus.Suppressed))
            incidentChanges.Add(ResolveAbsorbedIncident(incident, request, payload.ActivityExecutionId, workItem, occurredAt));

        return plan with { IncidentChanges = incidentChanges };
    }

    private static IncidentState ResolveAbsorbedIncident(
        IncidentState incident,
        RuntimeChildFaultAbsorptionRequest request,
        string absorbingActivityExecutionId,
        RuntimeSchedulerWorkItem workItem,
        DateTimeOffset resolvedAt)
    {
        var metadata = incident.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.FaultAbsorbedBy] = absorbingActivityExecutionId;
        metadata[RuntimeMetadataKeys.FaultAbsorptionReason] = request.Reason;
        metadata[RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId;

        return new IncidentState(
            incident.IncidentId,
            incident.WorkflowExecutionId,
            incident.ActivityExecutionId,
            incident.ExecutableNodeId,
            incident.Severity,
            IncidentStatus.Resolved,
            IncidentResolutionAction.Continue,
            incident.FailureType,
            incident.Message,
            incident.CreatedAt,
            resolvedAt,
            metadata);
    }

    private static Dictionary<string, string> NewStagedCancellationMetadata(
        RuntimeSchedulerWorkItem workItem,
        string reason,
        string requestedByActivityExecutionId,
        IReadOnlyDictionary<string, string> requestMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.ScopeCancellationReason] = reason,
            [RuntimeMetadataKeys.SubtreeCancellationRequestedBy] = requestedByActivityExecutionId
        };
        foreach (var item in requestMetadata)
            metadata[item.Key] = item.Value;
        return metadata;
    }

    private async ValueTask RecordParentFaultAsync(
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        ActivityExecutionState fallbackState,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var latestFaultedParentState = await activityExecutionStateStore.FindAsync(
                                           workItem.WorkflowExecutionId,
                                           payload.ActivityExecutionId,
                                           cancellationToken)
                                       ?? fallbackState;
        var request = NewFaultIncidentRecordRequest(
            checkpointCommitter,
            workItem,
            payload,
            latestFaultedParentState,
            exception,
            subStatus,
            valueSnapshots);

        // The faulting node is the parent composite. Ride a child-fault evaluation for its own parent on the
        // incident checkpoint so a grandparent join resolves instead of waiting forever. A root has no such work.
        var incidentId = ActivityFaultIncidentRecorder.IncidentId(
            workItem.WorkItemId,
            payload.ActivityExecutionId,
            subStatus);
        var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
            activityExecutionStateStore,
            _timeProvider,
            workItem,
            payload.PinnedExecutable,
            latestFaultedParentState,
            incidentId,
            cancellationToken);
        await activityFaultIncidentRecorder.CommitAsync(
            parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
            cancellationToken);
    }

    private async ValueTask<ConstructedActivity> ConstructActivityAsync(
        IServiceProvider serviceProvider,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        var contract = executableNode.ActivityContract
            ?? throw new InvalidOperationException($"VF-ACT-001: Executable CLR activity node '{executableNode.ExecutableNodeId}' has no pinned activity contract.");
        state.EnsureValueFlowCompatible();
        var snapshot = RequireCommittedSnapshot(state, contract);
        var attempt = state.Attempts?.LastOrDefault(item => item.EndedAt is null)
            ?? throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{state.InvocationId}' has no open committed attempt.");
        var activationLease = await serviceProvider.GetRequiredService<IActivityActivator>().ActivateAsync(
            new ActivityActivationRequest(contract, snapshot, attempt, state.PrivateState, Descriptor: executableNode.Descriptor),
            cancellationToken);
        return new ConstructedActivity(activationLease.Activity, snapshot, activationLease);
    }

    private static ActivityInputSnapshot RequireCommittedSnapshot(ActivityExecutionState state, ActivityContract contract)
    {
        var snapshot = state.InputSnapshot
            ?? throw new InvalidOperationException($"VF-ACT-009: Typed activity invocation '{state.InvocationId}' has no committed input snapshot.");

        if (!StringComparer.Ordinal.Equals(snapshot.InvocationId, state.InvocationId) ||
            !StringComparer.Ordinal.Equals(snapshot.ContractFingerprint, contract.SchemaFingerprint))
            throw new InvalidOperationException($"VF-ACT-001: Typed activity invocation '{state.InvocationId}' does not match its pinned input snapshot contract.");

        if (state.ContractIdentity is not { } identity ||
            !StringComparer.Ordinal.Equals(identity.ActivityTypeKey, contract.ActivityTypeKey) ||
            !StringComparer.Ordinal.Equals(identity.ContractVersion, contract.ContractVersion) ||
            !StringComparer.Ordinal.Equals(identity.SchemaFingerprint, contract.SchemaFingerprint))
            throw new InvalidOperationException($"VF-ACT-001: Typed activity invocation '{state.InvocationId}' does not match its pinned activity contract.");

        return snapshot;
    }

    private IEnumerable<RuntimeSchedulerWorkItem> NewChildActivityScheduleWorkItems(
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests)
    {
        var requests = scheduleRequests.ToArray();
        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            var now = _timeProvider.GetUtcNow();
            var childActivityExecutionId = idGenerator.NewActivityExecutionId();
            var payload = new RuntimeScheduleActivityCommandPayload(
                parentCompletionPayload.PinnedExecutable,
                request.ExecutableNodeId,
                childActivityExecutionId,
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                request.SchedulingActivityExecutionId ?? parentCompletionPayload.ActivityExecutionId,
                parentCompletionPayload.ActivityExecutionId,
                request.SchedulingProvenance == ActivitySchedulingProvenance.Empty
                    ? ActivitySchedulingProvenance.From(
                        parentCompletionWorkItem.WorkflowExecutionId,
                        parentCompletionPayload.ActivityExecutionId,
                        request.SchedulingActivityExecutionId ?? parentCompletionPayload.ActivityExecutionId,
                        branchId: null,
                        iterationId: null,
                        executionPathId: null,
                        executionScopeId: null,
                        schedulingCause: RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                        metadata: request.Metadata)
                    : request.SchedulingProvenance,
                request.IterationFrame);

            var commandMetadata = parentCompletionWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            foreach (var item in request.Metadata)
                commandMetadata[item.Key] = item.Value;

            commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = parentCompletionPayload.ActivityExecutionId;
            commandMetadata[RuntimeMetadataKeys.ChildExecutableNodeId] = request.ExecutableNodeId;

            var workItem = new RuntimeSchedulerWorkItem(
                workItemId: RuntimeChainId.Derive(parentCompletionWorkItem.WorkItemId, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                workflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
                commandId: RuntimeChainId.Derive(parentCompletionWorkItem.CommandId, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                envelopeId: parentCompletionWorkItem.EnvelopeId,
                idempotencyKey: RuntimeChainId.Derive(parentCompletionWorkItem.IdempotencyKey, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                enqueuedAt: now,
                recordedAt: now,
                sequence: parentCompletionWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: commandMetadata,
                envelopeMetadata: parentCompletionWorkItem.EnvelopeMetadata);

            yield return workItem;
        }
    }

    /// <summary>
    /// Projects planned subtree cancellations (spec 112) onto the typed change-set slots of the one
    /// commit that also persists the parent's continuation (FR-7 single-commit atomicity).
    /// </summary>
    private static async ValueTask<SubtreeCancellationCommitChanges> BuildSubtreeCancellationChangesAsync(
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> plans,
        string checkpointId,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
            return SubtreeCancellationCommitChanges.Empty;

        var cancelledStates = plans.SelectMany(plan => plan.CancelledStates).ToArray();
        var activityExecutionChanges = cancelledStates
            .Select(state => new RuntimeStateChange<ActivityExecutionState>(
                state.Execution.ActivityExecutionId,
                RuntimeStateChangeOperation.Upsert,
                state,
                metadata))
            .ToArray();
        var incidentChanges = plans.SelectMany(plan => plan.IncidentChanges)
            .Select(incident => new RuntimeStateChange<IncidentState>(
                incident.IncidentId,
                RuntimeStateChangeOperation.Upsert,
                incident,
                metadata))
            .ToArray();
        var inspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>(cancelledStates.Length);
        if (inspectionAccumulator is not null)
        {
            foreach (var state in cancelledStates)
            {
                var projection = await inspectionAccumulator.BuildProjectionAsync(
                    state, checkpointId, occurredAt, metadata: metadata, cancellationToken: cancellationToken);
                inspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                    state.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    projection,
                    metadata));
            }
        }

        return new SubtreeCancellationCommitChanges(
            activityExecutionChanges,
            incidentChanges,
            inspectionChanges,
            plans.Select(plan => plan.Cleanup).ToArray(),
            cancelledStates.Select(state => state.Execution.ActivityExecutionId).ToArray());
    }

    private sealed record SubtreeCancellationCommitChanges(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> Incidents,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> Inspections,
        IReadOnlyCollection<ActivityScopeCleanupRequest> Cleanups,
        IReadOnlyCollection<string> CancelledActivityExecutionIds)
    {
        public static readonly SubtreeCancellationCommitChanges Empty = new([], [], [], [], []);
    }

    private async ValueTask CommitDeferredParentActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        ActivityExecutionState parentState,
        ActivityExecutionState processedChildState,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        IReadOnlyCollection<RuntimeSchedulerWorkItem> continuationWorkItems,
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> subtreeCancellations,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{parentCompletionWorkItem.WorkItemId}:activity-inspection-captured:{parentCompletionPayload.ActivityExecutionId}";
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = parentCompletionWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = parentCompletionWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = "ChildActivityScheduling",
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = parentCompletionPayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = parentCompletionPayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = parentCompletionPayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = parentCompletionPayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = parentCompletionPayload.PinnedExecutable.ArtifactHash
        };
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> inspectionChanges = inspectionAccumulator is null
            ? []
            :
            [
                new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                    StateId: parentCompletionPayload.ActivityExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: await inspectionAccumulator.BuildProjectionAsync(
                        parentState,
                        checkpointId,
                        occurredAt,
                        valueSnapshots: valueSnapshots,
                        metadata: metadata,
                        cancellationToken: cancellationToken),
                    Metadata: metadata)
            ];
        var childWorkItems = NewChildActivityScheduleWorkItems(idGenerator, parentCompletionWorkItem, parentCompletionPayload, scheduleRequests).ToArray();
        var cancellationChanges = await BuildSubtreeCancellationChangesAsync(
            inspectionAccumulator, subtreeCancellations, checkpointId, occurredAt, metadata, cancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{parentCompletionWorkItem.WorkItemId}:activity-inspection-captured:{parentCompletionPayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds:
                [
                    parentCompletionPayload.ActivityExecutionId,
                    processedChildState.Execution.ActivityExecutionId,
                    .. cancellationChanges.CancelledActivityExecutionIds
                ],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: parentCompletionPayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: parentState,
                        Metadata: metadata),
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: processedChildState.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: processedChildState,
                        Metadata: metadata),
                    .. cancellationChanges.ActivityExecutions
                ],
                bookmarks: [],
                durableValues: [],
                incidents: cancellationChanges.Incidents,
                operational: [],
                activityExecutionInspections: [.. inspectionChanges, .. cancellationChanges.Inspections],
                activityScopeCleanups: cancellationChanges.Cleanups),
            PostCommitIntents: childWorkItems
                .Concat(continuationWorkItems)
                .Select(workItem => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(parentCompletionWorkItem, parentCompletionPayload.ActivityExecutionId, workItem, occurredAt))
                .ToArray(),
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private async ValueTask CommitCompletedParentActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        ActivityExecutionState completedParentState,
        IReadOnlyCollection<string> outcomeNames,
        IReadOnlyCollection<ActivitySubtreeCancellationPlan> subtreeCancellations,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{parentCompletionWorkItem.WorkItemId}:parent-activity-completed:{parentCompletionPayload.ActivityExecutionId}";
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = parentCompletionWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = parentCompletionWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = parentCompletionPayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = parentCompletionPayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = parentCompletionPayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = parentCompletionPayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = parentCompletionPayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = parentCompletionPayload.PinnedExecutable.ArtifactHash
        };
        var inspection = inspectionAccumulator is null
            ? null
            : await inspectionAccumulator.BuildProjectionAsync(
                completedParentState,
                checkpointId,
                occurredAt,
                outcomeNames: outcomeNames,
                valueSnapshots: valueSnapshots,
                metadata: metadata,
                cancellationToken: cancellationToken);
        var completionWorkItem = NewCompletionWorkItem(parentCompletionWorkItem, parentCompletionPayload, completedParentState);
        var cancellationChanges = await BuildSubtreeCancellationChangesAsync(
            inspectionAccumulator, subtreeCancellations, checkpointId, occurredAt, metadata, cancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{parentCompletionWorkItem.WorkItemId}:parent-activity-completed:{parentCompletionPayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCompleted,
                WorkflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [parentCompletionPayload.ActivityExecutionId, .. cancellationChanges.CancelledActivityExecutionIds],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: parentCompletionPayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: completedParentState,
                        Metadata: metadata),
                    .. cancellationChanges.ActivityExecutions
                ],
                bookmarks: [],
                durableValues: durableValueChanges,
                incidents: cancellationChanges.Incidents,
                operational: [],
                activityExecutionInspections: inspection is null
                    ?
                    [
                        .. cancellationChanges.Inspections
                    ]
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: parentCompletionPayload.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata),
                        .. cancellationChanges.Inspections
                    ],
                activityScopeCleanups: cancellationChanges.Cleanups),
            PostCommitIntents: [SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(parentCompletionWorkItem, parentCompletionPayload.ActivityExecutionId, completionWorkItem, occurredAt)],
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private RuntimeSchedulerWorkItem NewCompletionWorkItem(
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        ActivityExecutionState completedParentState)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeCompleteActivityCommandPayload(
            parentCompletionPayload.PinnedExecutable,
            parentCompletionPayload.ExecutableNodeId,
            parentCompletionPayload.ActivityExecutionId,
            completedParentState.ParentActivityExecutionId,
            completedParentState.BranchId,
            ReadCompletionOutcomeNames(completedParentState),
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(parentCompletionWorkItem.WorkItemId, $"complete:{parentCompletionPayload.ActivityExecutionId}"),
            workflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(parentCompletionWorkItem.CommandId, $"complete:{parentCompletionPayload.ActivityExecutionId}"),
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: parentCompletionWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(parentCompletionWorkItem.IdempotencyKey, $"complete:{parentCompletionPayload.ActivityExecutionId}"),
            enqueuedAt: now,
            recordedAt: now,
            sequence: parentCompletionWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: parentCompletionWorkItem.CommandMetadata,
            envelopeMetadata: parentCompletionWorkItem.EnvelopeMetadata);
    }

    private RuntimeSchedulerWorkItem NewContinuationSchedulingWorkItem(
        RuntimeSchedulerWorkItem sourceWorkItem,
        RuntimeCompleteActivityCommandPayload sourcePayload)
    {
        var now = _timeProvider.GetUtcNow();
        var activityExecutionId = sourcePayload.ActivityExecutionId;
        var payload = new RuntimeCompleteActivityCommandPayload(
            sourcePayload.PinnedExecutable,
            sourcePayload.ExecutableNodeId,
            activityExecutionId,
            sourcePayload.ParentActivityExecutionId,
            sourcePayload.BranchId,
            sourcePayload.OutcomeNames,
            RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
            SchedulerCompletionKind.ContinuationScheduling);

        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(sourceWorkItem.WorkItemId, $"continuation:{activityExecutionId}"),
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(sourceWorkItem.CommandId, $"continuation:{activityExecutionId}"),
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: sourceWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(sourceWorkItem.IdempotencyKey, $"continuation:{activityExecutionId}"),
            enqueuedAt: now,
            recordedAt: now,
            sequence: sourceWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: sourceWorkItem.CommandMetadata,
            envelopeMetadata: sourceWorkItem.EnvelopeMetadata);
    }

    private ActivityExecutionState CompleteParentActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
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

    // True when this parent-evaluation work item was raised by a child fault (vs. a child completion). Set by
    // ChildFaultParentEvaluation when it propagates the fault to the parent (#308).
    private static bool IsChildFaulted(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.ChildFaulted, out var value)
        && bool.TryParse(value, out var faulted) && faulted;

    private static string? ReadIncidentId(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.IncidentId, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static RuntimeCompleteActivityCommandPayload DeserializeCompletePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "CompleteActivity scheduler work item requires a complete activity payload.",
            resolvedToNullMessage: "CompleteActivity scheduler work item payload resolved to null.",
            invalidPayloadMessage: "CompleteActivity scheduler work item payload is not a valid complete activity payload.",
            // RT-11: reuse the single per-work-item parse rather than deserializing the payload again.
            deserialize: static (item, _) => RuntimeCompleteActivityPayloadMemo.Deserialize(item),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException ||
                exception is ArgumentException argumentException && IsCompletePayloadValidationException(argumentException));

    private static bool IsCompletePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "parentActivityExecutionId" or
            "branchId" or
            "outcomeNames" or
            "reason" or
            "completionKind" or
            "completedChildActivityExecutionId";

    private static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.ParentCompletionReason] = payload.Reason,
            [RuntimeMetadataKeys.ParentCompletionSchedulerWorkItemId] = workItem.WorkItemId
        };

        if (payload.CompletedChildActivityExecutionId is { } completedChildActivityExecutionId)
            activityMetadata[RuntimeMetadataKeys.CompletedChildActivityExecutionId] = completedChildActivityExecutionId;

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

    private static IReadOnlyCollection<string> ReadCompletionOutcomeNames(ActivityExecutionState completedState)
    {
        if (completedState.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(outcomeNames, defaultToDone: false);
        }

        return [ActivityOutcomes.Done];
    }

    private sealed record ConstructedActivity(
        IActivity Activity,
        ActivityInputSnapshot InputSnapshot,
        ActivityActivationLease ActivationLease);
}

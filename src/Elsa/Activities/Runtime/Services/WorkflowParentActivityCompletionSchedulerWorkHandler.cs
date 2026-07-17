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
        var workflowExecutableStore = serviceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();

        var executable = await workflowExecutableStore.FindAsync(payload.PinnedExecutable.ArtifactId, cancellationToken);
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
        ActivityExecutionState? callbackBypassParentState = null;
        IReadOnlyCollection<RuntimeSchedulerWorkItem> callbackBypassContinuationWorkItems = [];
        ActivityActivationLease? activationLease = null;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots = [];
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
                context = SimpleActivityExecutionContext.ForExecution(
                    parentActivity,
                    cancellationToken,
                    workItem.WorkflowExecutionId,
                    payload.PinnedExecutable,
                    workItem,
                    parentExecutableNode,
                    parentState,
                    variableScope: null);

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
                valueSnapshots,
                cancellationToken);
            return;
        }

        ActivityExecutionState completedParentState;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> containerVariableSnapshots;
        try
        {
            var completedAt = _timeProvider.GetUtcNow();
            var contract = parentExecutableNode.ActivityContract
                ?? throw new InvalidOperationException($"VF-ACT-001: Executable structural activity node '{parentExecutableNode.ExecutableNodeId}' has no pinned activity contract.");
            var openAttempt = currentParentState.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)
                ?? throw new InvalidOperationException($"VF-ACT-009: Running structural activity invocation '{currentParentState.InvocationId}' has no open committed attempt.");
            var completionProjection = await serviceProvider.GetRequiredService<ActivityCompletionProjector>().ProjectAsync(
                workItem.WorkflowExecutionId,
                currentParentState.InvocationId,
                openAttempt,
                contract,
                ActivityTransition.Complete(ActivityUnit.Value, resolvedContinuation.OutcomeName!),
                completedAt,
                cancellationToken);
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

        await CommitCompletedParentActivityAsync(checkpointCommitter, inspectionAccumulator, workItem, payload, completedParentState, ReadCompletionOutcomeNames(completedParentState), containerVariableSnapshots, cancellationToken);
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
            new ActivityActivationRequest(contract, snapshot, attempt, state.PrivateState),
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
                workItemId: $"{parentCompletionWorkItem.WorkItemId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                workflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
                commandId: $"{parentCompletionWorkItem.CommandId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                envelopeId: parentCompletionWorkItem.EnvelopeId,
                idempotencyKey: $"{parentCompletionWorkItem.IdempotencyKey}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                enqueuedAt: now,
                recordedAt: now,
                sequence: parentCompletionWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: commandMetadata,
                envelopeMetadata: parentCompletionWorkItem.EnvelopeMetadata);

            yield return workItem;
        }
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
                    processedChildState.Execution.ActivityExecutionId
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
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: inspectionChanges),
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
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
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
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{parentCompletionWorkItem.WorkItemId}:parent-activity-completed:{parentCompletionPayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCompleted,
                WorkflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [parentCompletionPayload.ActivityExecutionId],
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
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: parentCompletionPayload.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata)
                    ]),
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
            workItemId: $"{parentCompletionWorkItem.WorkItemId}:complete:{parentCompletionPayload.ActivityExecutionId}",
            workflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
            commandId: $"{parentCompletionWorkItem.CommandId}:complete:{parentCompletionPayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: parentCompletionWorkItem.EnvelopeId,
            idempotencyKey: $"{parentCompletionWorkItem.IdempotencyKey}:complete:{parentCompletionPayload.ActivityExecutionId}",
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
            workItemId: $"{sourceWorkItem.WorkItemId}:continuation:{activityExecutionId}",
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            commandId: $"{sourceWorkItem.CommandId}:continuation:{activityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: sourceWorkItem.EnvelopeId,
            idempotencyKey: $"{sourceWorkItem.IdempotencyKey}:continuation:{activityExecutionId}",
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

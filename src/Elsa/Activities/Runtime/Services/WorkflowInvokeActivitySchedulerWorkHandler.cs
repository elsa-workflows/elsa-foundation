using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

public sealed class WorkflowInvokeActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowInvokeActivitySchedulerWorkHandler);
    private const string SkippedSubStatus = "Skipped";

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the handler. RT-8: collapsed to a single primary constructor (the former ambient-services-accessor
    /// overload is gone — RT-7 replaced that AsyncLocal service locator with the explicit
    /// <see cref="IRuntimePipelineContext"/> workspace carrier).
    /// </summary>
    public WorkflowInvokeActivitySchedulerWorkHandler(
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

        return workItem.CommandKind == WorkflowExecutionCommandKind.InvokeActivity;
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
    /// (staged explicitly by the dispatcher) instead of an AsyncLocal service locator. The nested-invoke commits stay
    /// inline through the resolved provider so W9 coalescing boundary detection and W5 fencing granularity are preserved
    /// exactly — this handler stages nothing, so the Checkpoint slot is a no-op for it.
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
        var invokePayload = DeserializeInvokePayload(workItem);
        if (ambientServices is { } provider)
        {
            await HandleWithServicesAsync(workItem, invokePayload, provider, cancellationToken);
            return;
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        await HandleWithServicesAsync(workItem, invokePayload, scope.ServiceProvider, cancellationToken);
    }

    private async ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var schedulerWorkQueue = serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

        // spec 111: burst-cached pinned-executable read (immutable artifact ⇒ one durable read per burst, not per hop).
        var executable = await PinnedExecutableRead.FindAsync(serviceProvider, invokePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(invokePayload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, invokePayload.PinnedExecutable, executable.Identity);

        var executableNode = SchedulerWorkHandlerHelpers.ResolveExecutableNode(workItem, executable, invokePayload.ExecutableNodeId, "InvokeActivity");

        var state = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' references missing activity execution '{invokePayload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, invokePayload.ExecutableNodeId))
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' references executable node '{invokePayload.ExecutableNodeId}', but activity execution '{invokePayload.ActivityExecutionId}' belongs to executable node '{state.Execution.ExecutableNodeId}'.");

        if (state.Status == ActivityExecutionStatus.Completed)
            return;

        if (state.Status != ActivityExecutionStatus.Running)
            return;

        if (ActivityAttemptActivationClaimer.WasInitialActivationCompleted(state, workItem.WorkItemId))
            return;

        state.EnsureValueFlowCompatible();

        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var checkpointCommitter = serviceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var activityFaultIncidentRecorder = serviceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var inspectionAccumulator = serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>();
        var payloadCapturePolicy = serviceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();
        await InvokeActivityAsync(serviceProvider, activityExecutionStateStore, schedulerWorkQueue, durableValueStateStore, checkpointCommitter, activityFaultIncidentRecorder, inspectionAccumulator, payloadCapturePolicy, workItem, invokePayload, executable, executableNode, state, cancellationToken);
    }

    private async ValueTask InvokeActivityAsync(
        IServiceProvider serviceProvider,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IDurableValueStateStore durableValueStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        var workflowDispatchStaging = serviceProvider.GetService<IWorkflowDispatchStagingAccessor>();
        workflowDispatchStaging?.Reset(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId);
        var scopeService = new RuntimeContainerScopeService(
            activityExecutionStateStore,
            serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>());

        RuntimeInputBindingStateProjectionSet projections;
        IReadOnlyDictionary<string, object?> workflowInputValues;
        IReadOnlyDictionary<string, object?> activityOutputValues;
        IReadOnlyCollection<DurableValueState> persistedDurableValues = [];
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges = [];

        // Carrier identity (ADR 0030: correlation id / instance name) is projected from the IdentityName-tagged durable
        // values this invocation already re-lists (spec 083 review), so a plain activity populates the carrier without
        // a per-invocation workflow-execution-state read. A Correlate/SetName leaf writes the new value as a durable
        // value (see the identity fold below), which every activity invocation — including a concurrent sibling
        // branch — re-lists, so cross-branch visibility holds. The control-leaf state change below is the only path
        // that loads the workflow-execution state, and only when an intent actually mutates it.
        try
        {
            var durableValues = await durableValueStateStore.ListAllDurableValueStatesAsync(workItem.WorkflowExecutionId, cancellationToken);
            persistedDurableValues = durableValues;
            projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
            workflowInputValues = projections.WorkflowInputs;
            activityOutputValues = projections.ActivityOutputValues;

            if (executableNode.ActivityContract is null)
                throw new InvalidOperationException($"VF-ACT-001: Executable CLR activity node '{executableNode.ExecutableNodeId}' has no pinned activity contract.");

            state.EnsureValueFlowCompatible();
            if (state.InputSnapshot is null)
                throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{state.InvocationId}' has no committed input snapshot.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFaultAsync(activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, workItem, invokePayload, state, exception, "InputMaterializationFailed", [], cancellationToken);
            return;
        }
        var valueSnapshots = new List<ActivityExecutionInspectionValueSnapshot>();
        IActivity activity;
        SimpleActivityExecutionContext context;
        ActivityActivationLease? activationLease = null;
        ActivityAttempt? valueFlowAttempt = null;
        ActivityInputSnapshot? valueFlowSnapshot = null;
        try
        {
            var activityContract = executableNode.ActivityContract!;
            valueSnapshots.AddRange(ActivityExecutionInspection.BuildInputValueSnapshots(
                payloadCapturePolicy,
                workItem,
                invokePayload,
                activityContract,
                state.InputSnapshot!,
                _timeProvider.GetUtcNow()));

            // Transient activation and one-time snapshot hydration run
            // inside the same fault boundary as input materialization (#317). Previously this step sat between
            // the two materialization try/catch blocks, so a binder/constructor throw (e.g. a typed-binding
            // InvalidOperationException) escaped to the scheduler loop and left the run silently at Running with
            // no incident. Recording it as a blocking incident faults the activity and surfaces a queryable cause.
            valueFlowSnapshot = state.InputSnapshot!;
            var activationClaim = await ActivityAttemptActivationClaimer.ClaimInvokeAsync(
                checkpointCommitter,
                _timeProvider,
                workItem,
                invokePayload,
                state,
                activityContract.SideEffectProfile,
                cancellationToken);
            state = activationClaim.State;
            valueFlowAttempt = activationClaim.Attempt;
            activationLease = await serviceProvider.GetRequiredService<IActivityActivator>().ActivateAsync(
                new ActivityActivationRequest(activityContract, valueFlowSnapshot, valueFlowAttempt, Descriptor: executableNode.Descriptor),
                cancellationToken);
            activity = activationLease.Activity;

            var executionContextState = valueFlowAttempt is null
                ? state
                : state with
                {
                    InputSnapshot = valueFlowSnapshot,
                    Attempts = (state.Attempts ?? [])
                        .Where(attempt => attempt.AttemptId != valueFlowAttempt.AttemptId)
                        .Append(valueFlowAttempt)
                        .ToArray()
                };
            // spec 123 D1: a structural activity that reads its enclosing container-scoped variable values (the
            // BpmnProcess) gets a committed name→envelope projection of its own visible frame chain; marker-gated,
            // so a non-consumer activity pays nothing and reads always return false.
            var scopedVariableEnvelopes = await scopeService.ProjectScopedVariablesForReaderAsync(
                activity, executable, executionContextState, cancellationToken);

            context = SimpleActivityExecutionContext.ForExecution(
                activity,
                cancellationToken,
                workItem.WorkflowExecutionId,
                invokePayload.PinnedExecutable,
                workItem,
                executableNode,
                executionContextState,
                variableScope: null,
                triggerPayload: projections.StimulusInput is JsonElement stimulusInput ? stimulusInput : null,
                triggerNodeId: projections.TriggerNodeId,
                triggerMetadata: projections.TriggerMetadata,
                scopedVariableEnvelopes: scopedVariableEnvelopes);
        }
        catch (OperationCanceledException cancellationException) when (cancellationToken.IsCancellationRequested)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            if (disposalException is not null)
                throw new AggregateException("Activity activation cancellation and disposal both failed.", cancellationException, disposalException);
            throw;
        }
        catch (Exception exception)
        {
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            var fault = disposalException is null
                ? exception
                : ActivityActivationLeaseDisposer.Combine(exception, disposalException);
            var subStatus = disposalException is null ? "ActivityConstructionFailed" : "ActivityDisposalFailed";
            await RecordFaultAsync(activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, workItem, invokePayload, state, fault, subStatus, valueSnapshots, cancellationToken);
            return;
        }

        ActivityExecutionState? completedState = null;
        (IRuntimeExecutionIdGenerator IdGenerator, IReadOnlyCollection<RuntimeChildActivityScheduleRequest> Requests)? pendingChildScheduling = null;
        RuntimeStructuralContinuation? structuralContinuation = null;
        ActivityTransition? returnedTransition = null;
        ActivityCompletionProjection? valueFlowCompletion = null;
        ActivityExecutionState? typedSuspendedState = null;
        WorkflowDispatchCheckpointRequest? stagedWorkflowDispatch = null;
        ActivityFault? returnedFault = null;
        string? returnedCancellationReason = null;
        IReadOnlyCollection<RuntimeSchedulerWorkItem> parentNotificationWorkItems = [];
        var stagedState = state;
        try
        {
            var checkpointParticipant = activity as IRuntimeActivityCheckpointParticipant;
            if (checkpointParticipant is not null)
            {
                var effectiveInputs = await MaterializeCheckpointInputsAsync(
                    valueFlowSnapshot!,
                    serviceProvider.GetService<IExternalPayloadStore>(),
                    cancellationToken);
                durableValueChanges = await checkpointParticipant.PrepareEntryCheckpointAsync(
                    context,
                    effectiveInputs,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }

            if (activity is IRuntimeStructuralActivity structuralActivity)
            {
                structuralContinuation = await structuralActivity.ExecuteStructureAsync(context);
                stagedState = RuntimeStructuralStateProjector.Apply(stagedState, structuralContinuation, _timeProvider.GetUtcNow());
                returnedTransition = structuralContinuation.Kind switch
                {
                    RuntimeStructuralContinuationKind.Complete => ActivityTransition.Complete(ActivityUnit.Value, structuralContinuation.OutcomeName!),
                    RuntimeStructuralContinuationKind.Fault => ActivityTransition.Fault<ActivityUnit>(structuralContinuation.Fault!),
                    RuntimeStructuralContinuationKind.Cancel => ActivityTransition.Cancel<ActivityUnit>(structuralContinuation.CancellationReason!),
                    RuntimeStructuralContinuationKind.Defer => null,
                    _ => throw new ArgumentOutOfRangeException(nameof(structuralContinuation.Kind), structuralContinuation.Kind, "Unknown structural continuation kind.")
                };
            }
            else
                returnedTransition = await activity.ExecuteAsync(context.ToActivityExecutionContext());

            var childScheduleRequests = context.GetChildActivityScheduleRequests();
            if (structuralContinuation is null && childScheduleRequests.Count > 0)
                throw new InvalidOperationException("Only an engine structural activity can schedule child activities.");
            if (structuralContinuation is { IsDeferred: false } && childScheduleRequests.Count > 0)
                throw new InvalidOperationException("A terminal structural decision cannot also schedule child activities in the same execution.");
            if (structuralContinuation?.IsDeferred == true && childScheduleRequests.Count == 0)
                throw new InvalidOperationException("An initial structural execution cannot defer without scheduling at least one child activity.");
            if (context.GetChildSubtreeCancellationRequests().Count > 0)
                throw new InvalidOperationException("An initial structural execution cannot cancel child subtrees; cancellation requests are only valid during a child-completion evaluation (spec 112).");
            if (context.GetChildFaultAbsorptionRequests().Count > 0)
                throw new InvalidOperationException("An initial structural execution cannot absorb child faults; absorption requests are only valid during a child-fault evaluation (spec 115).");

            // spec 126 seam C: a non-root structural child may notify its own parent during its initial
            // structural execution. Harvest the staged notifications now so a root staging or a Fault/Cancel
            // continuation with staged notifications faults the evaluation inside this callback boundary; the
            // built work items ride the same Defer/Complete commit below.
            parentNotificationWorkItems = await ParentNotificationEvaluation.BuildAsync(
                activityExecutionStateStore,
                _timeProvider,
                workItem,
                invokePayload.PinnedExecutable,
                stagedState,
                context.GetParentNotificationRequests(),
                structuralContinuation ?? RuntimeStructuralContinuation.Defer,
                cancellationToken);

            if (returnedTransition is IStatefulActivitySuspensionTransition statefulSuspension)
            {
                if (valueFlowAttempt is null)
                    throw new InvalidOperationException("A stateful suspension requires a pinned typed activity contract and active attempt.");

                if (childScheduleRequests.Count > 0 || structuralContinuation is not null)
                    throw new InvalidOperationException("A stateful suspension transition cannot also participate in structural execution.");

                ValidateStatefulSuspensionRegistrations(executable, executableNode, statefulSuspension);
                typedSuspendedState = StatefulActivitySuspensionProjector.Project(
                    stagedState,
                    valueFlowAttempt,
                    statefulSuspension,
                    _timeProvider.GetUtcNow(),
                    key => ResolveResumeTarget(executable, executableNode, key).ResumeTargetId);
            }
            else if (returnedTransition is IActivityFaultTransition faultTransition)
            {
                returnedFault = faultTransition.Fault;
            }
            else if (returnedTransition is IActivityCancellationTransition cancellationTransition)
            {
                returnedCancellationReason = cancellationTransition.Reason;
            }
            else if (childScheduleRequests.Count > 0)
            {
                var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();
                stagedState = ActivityAttemptActivationClaimer.EndOpenAttempt(
                    stagedState,
                    Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend,
                    _timeProvider.GetUtcNow());
                stagedState = ActivityAttemptActivationClaimer.MarkInitialActivationCompleted(stagedState, workItem.WorkItemId);
                pendingChildScheduling = (idGenerator, childScheduleRequests);
            }
            else if (structuralContinuation?.IsDeferred != true)
            {
                if (checkpointParticipant is not null)
                {
                    var completionPreparation = await checkpointParticipant.PrepareCompletionCheckpointAsync(
                        context,
                        ApplyDurableValueChanges(persistedDurableValues, durableValueChanges),
                        _timeProvider.GetUtcNow(),
                        cancellationToken);
                    var completionTransition = (IActivityCompletionTransition)completionPreparation.Transition;
                    if (structuralContinuation?.IsComplete == true &&
                        !StringComparer.Ordinal.Equals(completionTransition.Outcome, structuralContinuation.OutcomeName))
                    {
                        throw new InvalidOperationException(
                            $"Checkpoint participant completion outcome '{completionTransition.Outcome}' does not match structural continuation outcome '{structuralContinuation.OutcomeName}'.");
                    }

                    returnedTransition = completionPreparation.Transition;
                    durableValueChanges = MergeDurableValueChanges(
                        durableValueChanges,
                        completionPreparation.DurableValueChanges);
                }

                var recordedOutputs = await ProjectReturnedCompletionAsync(executableNode.ActivityContract!);
                if (recordedOutputs.Count > 0)
                {
                    var recordedAt = _timeProvider.GetUtcNow();
                    valueSnapshots.AddRange(ActivityExecutionInspection.BuildOutputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, executableNode, recordedOutputs, recordedAt));
                }

                string[] outcomeNames = valueFlowCompletion is not null
                    ? [valueFlowCompletion.Completion.OutcomeKey]
                    : structuralContinuation?.IsComplete == true
                        ? [structuralContinuation.OutcomeName!]
                        : [ActivityOutcomes.Done];
                completedState = CompleteActivity(workItem, invokePayload, stagedState, outcomeNames, skipped: false);
                if (valueFlowCompletion is not null)
                {
                    var endedAt = completedState.CompletedAt ?? _timeProvider.GetUtcNow();
                    var completedAttempt = new ActivityAttempt(
                        valueFlowAttempt!.AttemptId,
                        valueFlowAttempt.InvocationId,
                        valueFlowAttempt.Ordinal,
                        valueFlowAttempt.Reason,
                        valueFlowAttempt.StartedAt,
                        endedAt,
                        valueFlowAttempt.TriggerDeliveryId,
                        Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete);
                    var priorAttempts = stagedState.Attempts?.Where(attempt => attempt.AttemptId != completedAttempt.AttemptId) ?? [];
                    completedState = completedState with
                    {
                        ContractIdentity = new ActivityInvocationContractIdentity(
                            executableNode.ActivityContract!.ActivityTypeKey,
                            executableNode.ActivityContract.ContractVersion,
                            executableNode.ActivityContract.SchemaFingerprint),
                        InputSnapshot = valueFlowSnapshot,
                        Attempts = priorAttempts.Append(completedAttempt).ToArray(),
                        Completion = valueFlowCompletion.Completion
                    };
                }

                // A completing container's scope is no longer live for runtime expressions; its
                // final variable values are retained as inspection evidence only through the
                // configured capture/retention policy (ADR 0027, #210).
                if (structuralContinuation?.IsComplete == true)
                {
                    var containerVariableSnapshots = RuntimeContainerVariableEvidence.Capture(
                        payloadCapturePolicy, scopeService, executableNode, stagedState,
                        workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, workItem.WorkItemId, _timeProvider.GetUtcNow());
                    if (containerVariableSnapshots.Count > 0)
                    {
                        valueSnapshots.AddRange(containerVariableSnapshots);
                        completedState = RuntimeContainerScopeService.CloseOwnedFrames(completedState);
                    }
                }
            }

            stagedWorkflowDispatch = workflowDispatchStaging?.TakeWorkflowDispatch(
                workItem.WorkflowExecutionId,
                invokePayload.ActivityExecutionId);
            if (stagedWorkflowDispatch is not null)
            {
                var expectedMode = typedSuspendedState is not null
                    ? WorkflowDispatchMode.WaitForCompletion
                    : returnedTransition is IActivityCompletionTransition
                        ? WorkflowDispatchMode.FireAndForget
                        : throw new InvalidOperationException(
                            "A workflow dispatch can be staged only with a successful completion or suspension transition.");
                if (stagedWorkflowDispatch.Record.Mode != expectedMode)
                {
                    throw new InvalidOperationException(
                        $"The staged workflow dispatch mode '{stagedWorkflowDispatch.Record.Mode}' does not match the activity transition.");
                }
            }
        }
        catch (OperationCanceledException cancellationException) when (cancellationToken.IsCancellationRequested)
        {
            workflowDispatchStaging?.Reset(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId);
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            if (disposalException is not null)
                throw new AggregateException("Activity execution cancellation and disposal both failed.", cancellationException, disposalException);
            throw;
        }
        catch (Exception exception)
        {
            workflowDispatchStaging?.Reset(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId);
            var disposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
            activationLease = null;
            var fault = disposalException is null
                ? exception
                : ActivityActivationLeaseDisposer.Combine(exception, disposalException);
            var subStatus = disposalException is null ? "ActivityFaulted" : "ActivityDisposalFailed";
            await RecordFaultAsync(activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, workItem, invokePayload, state, fault, subStatus, valueSnapshots, cancellationToken);
            return;
        }

        var activationDisposalException = await ActivityActivationLeaseDisposer.TryDisposeAsync(activationLease);
        activationLease = null;
        if (activationDisposalException is not null)
        {
            await RecordFaultAsync(
                activityFaultIncidentRecorder,
                activityExecutionStateStore,
                checkpointCommitter,
                workItem,
                invokePayload,
                state,
                activationDisposalException,
                "ActivityDisposalFailed",
                valueSnapshots,
                cancellationToken);
            return;
        }

        state = stagedState;

        async ValueTask<IReadOnlyCollection<RecordedActivityOutput>> ProjectReturnedCompletionAsync(ActivityContract activityContract)
        {
            if (returnedTransition is null || valueFlowAttempt is null)
                throw new InvalidOperationException($"Typed activity invocation '{invokePayload.ActivityExecutionId}' returned no transition.");

            valueFlowCompletion = await serviceProvider.GetRequiredService<ActivityCompletionProjector>().ProjectAsync(
                workItem.WorkflowExecutionId,
                invokePayload.ActivityExecutionId,
                valueFlowAttempt,
                activityContract,
                returnedTransition,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            var outputCaptureChanges = await serviceProvider.GetRequiredService<RuntimeOutputCaptureProjector>().ProjectAsync(
                workItem.WorkflowExecutionId,
                invokePayload.ActivityExecutionId,
                executableNode,
                (IActivityCompletionTransition)returnedTransition,
                valueFlowCompletion,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            durableValueChanges = MergeDurableValueChanges(durableValueChanges, outputCaptureChanges);
            return valueFlowCompletion.Projections
                .Where(item => item.Value.Presence != ValuePresence.Absent && item.Value.Policy.Storage != DurableValueStorage.External)
                .Select(item => new RecordedActivityOutput(
                    item.Key,
                    item.Value.Presence == ValuePresence.ExplicitNull ? null : item.Value.InlineValue))
                .ToArray();
        }

        if (returnedFault is not null)
        {
            var faultedState = state with
            {
                Fault = new NormalizedActivityFault(
                    returnedFault.Code,
                    typeof(ActivityFault).FullName!,
                    returnedFault.Message,
                    sanitizedStackTrace: null,
                    returnedFault.IsRetryable)
            };
            await RecordFaultAsync(
                activityFaultIncidentRecorder,
                activityExecutionStateStore,
                checkpointCommitter,
                workItem,
                invokePayload,
                faultedState,
                new ActivityTransitionFaultException(returnedFault),
                "ActivityReturnedFault",
                valueSnapshots,
                cancellationToken);
            return;
        }

        if (returnedCancellationReason is not null)
        {
            await ActivityCancellationCheckpointService.CommitAsync(
                checkpointCommitter,
                inspectionAccumulator,
                _timeProvider,
                workItem,
                state,
                returnedCancellationReason,
                valueSnapshots,
                cancellationToken: cancellationToken);
            return;
        }

        if (typedSuspendedState is not null)
        {
            await CommitStatefulSuspensionAsync(
                checkpointCommitter,
                inspectionAccumulator,
                serviceProvider.GetService<BookmarkLifecycleNotifier>(),
                workItem,
                invokePayload,
                typedSuspendedState,
                valueSnapshots,
                stagedWorkflowDispatch,
                cancellationToken);
            return;
        }

        if (pendingChildScheduling is { } childScheduling)
        {
            await CommitChildSchedulingActivityAsync(
                checkpointCommitter,
                inspectionAccumulator,
                childScheduling.IdGenerator,
                workItem,
                invokePayload,
                state,
                childScheduling.Requests,
                parentNotificationWorkItems,
                valueSnapshots,
                durableValueChanges,
                cancellationToken);
            return;
        }

        if (structuralContinuation?.IsDeferred == true)
            return;

        if (completedState is null)
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' did not produce a completion or child scheduling result for activity execution '{invokePayload.ActivityExecutionId}'.");

        var occurredAt = _timeProvider.GetUtcNow();

        await CommitCompletedActivityAsync(
            checkpointCommitter,
            inspectionAccumulator,
            workItem,
            invokePayload,
            completedState,
            ReadCompletionOutcomeNames(completedState),
            parentNotificationWorkItems,
            valueSnapshots,
            durableValueChanges,
            stagedWorkflowDispatch,
            occurredAt,
            cancellationToken);
    }

    // Records a blocking fault incident for the activity and commits it. Each fault arm in InvokeActivityAsync
    // (input materialization, construction/binding, execution) differs only in its reason and snapshot set;
    // centralizing the request shape + commit here keeps those arms to one call. When the faulted activity has a
    // parent fork/join, it also rides a child-fault parent-evaluation work item along on the incident checkpoint so
    // the parent can resolve its join deterministically (#308) instead of waiting forever for a completion that
    // never arrives. Parents that do not implement IRuntimeActivityChildFaultHandler no-op on that work item, so the fault
    // remains a plain blocking incident for sequential containers.
    private async ValueTask RecordFaultAsync(
        ActivityFaultIncidentRecorder activityFaultIncidentRecorder,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        CancellationToken cancellationToken)
    {
        var request = ActivityExecutionInspection.NewFaultIncidentRecordRequest(checkpointCommitter, workItem, invokePayload, state, exception, subStatus, valueSnapshots);
        var incidentId = ActivityFaultIncidentRecorder.IncidentId(workItem.WorkItemId, invokePayload.ActivityExecutionId, subStatus);
        var parentEvaluation = await ChildFaultParentEvaluation.TryBuildAsync(
            activityExecutionStateStore, _timeProvider, workItem, invokePayload.PinnedExecutable, state, incidentId, cancellationToken);

        await activityFaultIncidentRecorder.CommitAsync(
            parentEvaluation is null ? request : request with { PostCommitSchedulerWorkItemsOrNull = [parentEvaluation] },
            cancellationToken);
    }

    private static void ValidateStatefulSuspensionRegistrations(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        IStatefulActivitySuspensionTransition suspension)
    {
        foreach (var registration in suspension.Registrations)
            ResolveResumeTarget(executable, executableNode, registration.ResumeTargetKey);
    }

    private static WorkflowExecutableResumeTarget ResolveResumeTarget(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        string resumeTargetKey)
    {
        var resumeTarget = SchedulerWorkHandlerHelpers.FindResumeTargetForNode(
            executable,
            executableNode.ExecutableNodeId,
            resumeTargetKey);
        if (resumeTarget is null)
        {
            throw new InvalidOperationException(
                $"Stateful activity '{executableNode.ExecutableNodeId}' registered missing resume target '{resumeTargetKey}'.");
        }

        if (!StringComparer.Ordinal.Equals(resumeTarget.ExecutableNodeId, executableNode.ExecutableNodeId))
        {
            throw new InvalidOperationException(
                $"Resume target '{resumeTargetKey}' belongs to executable node '{resumeTarget.ExecutableNodeId}', not '{executableNode.ExecutableNodeId}'.");
        }

        return resumeTarget;
    }

    private async ValueTask CommitStatefulSuspensionAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        BookmarkLifecycleNotifier? bookmarkLifecycleNotifier,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState suspendedState,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        WorkflowDispatchCheckpointRequest? workflowDispatch,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{invokeWorkItem.WorkItemId}:activity-suspended:{invokePayload.ActivityExecutionId}";
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = invokeWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = invokeWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = invokePayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = invokePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = invokePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = invokePayload.PinnedExecutable.ArtifactHash
        };
        var inspection = inspectionAccumulator is null
            ? null
            : await inspectionAccumulator.BuildProjectionAsync(
                suspendedState,
                checkpointId,
                occurredAt,
                valueSnapshots: valueSnapshots,
                metadata: metadata,
                cancellationToken: cancellationToken);
        var bookmarkRequests = suspendedState.TriggerRegistrations!
            .Select(registration => new ActivityBookmarkRequest(
                registration.RegistrationId,
                registration.ResumeTargetKey,
                registration.StimulusType,
                registration.StimulusHash,
                metadata: registration.Metadata))
            .ToArray();
        BookmarkState[] bookmarks = [];
        RuntimeStateChange<WorkflowDispatchRecord>[] workflowDispatches = [];
        RuntimePostCommitIntent[] workflowDispatchIntents = [];
        RuntimeSchedulerWorkItem[] bookmarkWorkItems;
        if (workflowDispatch is null)
        {
            bookmarkWorkItems = NewBookmarkCreationWorkItems(
                    invokeWorkItem,
                    invokePayload,
                    bookmarkRequests)
                .ToArray();
        }
        else
        {
            var waitBookmark = workflowDispatch.WaitBookmark
                ?? throw new InvalidOperationException("A suspended workflow dispatch requires its canonical wait bookmark.");
            var registration = AssertSingleDispatchRegistration(suspendedState, waitBookmark);
            var reboundRegistration = new Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration(
                waitBookmark.BookmarkId,
                registration.InvocationId,
                registration.ResumeTargetKey,
                registration.PayloadType,
                registration.StimulusType,
                registration.StimulusHash,
                registration.DeduplicationPolicy,
                registration.Metadata);
            var bookmarkMetadata = waitBookmark.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            bookmarkMetadata[RuntimeMetadataKeys.SchedulerWorkItemId] = invokeWorkItem.WorkItemId;
            bookmarkMetadata[RuntimeMetadataKeys.CommandId] = invokeWorkItem.CommandId;
            bookmarkMetadata[RuntimeMetadataKeys.Reason] = RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason;
            var activityMetadata = suspendedState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            activityMetadata[RuntimeMetadataKeys.BookmarkId] = waitBookmark.BookmarkId;
            activityMetadata[RuntimeMetadataKeys.ResumeTargetId] = registration.ResumeTargetKey;
            activityMetadata[RuntimeMetadataKeys.SuspendReason] = RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason;
            suspendedState = suspendedState with
            {
                SubStatus = BookmarkSuspension.SuspendedSubStatus,
                BookmarkIds = [waitBookmark.BookmarkId],
                TriggerRegistrations = [reboundRegistration],
                Metadata = RuntimeModelMetadata.Snapshot(activityMetadata)
            };
            bookmarks =
            [
                new BookmarkState(
                    waitBookmark.BookmarkId,
                    invokeWorkItem.WorkflowExecutionId,
                    invokePayload.ActivityExecutionId,
                    invokePayload.ExecutableNodeId,
                    registration.ResumeTargetKey,
                    waitBookmark.StimulusType,
                    waitBookmark.StimulusHash,
                    waitBookmark.Payload,
                    RuntimeModelMetadata.Snapshot(bookmarkMetadata),
                    occurredAt,
                    waitBookmark.ExpiresAt)
            ];
            workflowDispatches =
            [
                new RuntimeStateChange<WorkflowDispatchRecord>(
                    workflowDispatch.Record.DispatchId,
                    RuntimeStateChangeOperation.Upsert,
                    workflowDispatch.Record,
                    metadata)
            ];
            workflowDispatchIntents = [workflowDispatch.StartIntent];
            bookmarkWorkItems = [];
        }
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{invokeWorkItem.WorkItemId}:activity-suspended:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: workflowDispatch is null
                    ? RuntimeCheckpointNames.ActivitySuspended
                    : RuntimeCheckpointNames.BookmarkCreated,
                WorkflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [invokePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: suspendedState,
                        Metadata: metadata)
                ],
                bookmarks: bookmarks
                    .Select(bookmark => new RuntimeStateChange<BookmarkState>(
                        bookmark.BookmarkId,
                        RuntimeStateChangeOperation.Upsert,
                        bookmark,
                        metadata))
                    .ToArray(),
                durableValues: [],
                incidents: [],
                operational: [],
                workflowDispatches: workflowDispatches,
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: invokePayload.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata)
                    ]),
            PostCommitIntents: bookmarkWorkItems
                .Select(workItem => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                    invokeWorkItem,
                    invokePayload.ActivityExecutionId,
                    workItem,
                    occurredAt))
                .Concat(workflowDispatchIntents)
                .ToArray(),
            Metadata: metadata);

        var commitResult = await checkpointCommitter.CommitAsync(commit, cancellationToken);
        if (!commitResult.Succeeded || bookmarkLifecycleNotifier is null)
            return;

        foreach (var bookmark in bookmarks)
            await bookmarkLifecycleNotifier.NotifyCreatedAsync(bookmark, CancellationToken.None);
    }

    private IEnumerable<RuntimeSchedulerWorkItem> NewBookmarkCreationWorkItems(
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<ActivityBookmarkRequest> bookmarkRequests)
    {
        var requests = bookmarkRequests.ToArray();
        if (requests.Select(request => request.BookmarkId).Distinct(StringComparer.Ordinal).Count() != requests.Length)
            throw new InvalidOperationException("Activity bookmark requests cannot contain duplicate bookmark IDs.");

        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            var now = _timeProvider.GetUtcNow();
            var payload = new RuntimeCreateBookmarkCommandPayload(
                pinnedExecutable: invokePayload.PinnedExecutable,
                bookmarkId: request.BookmarkId,
                activityExecutionId: invokePayload.ActivityExecutionId,
                executableNodeId: invokePayload.ExecutableNodeId,
                resumeTargetId: request.ResumeTargetId,
                stimulusType: request.StimulusType,
                stimulusHash: request.StimulusHash,
                payload: request.Payload,
                expiresAt: request.ExpiresAt,
                reason: RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
                metadata: request.Metadata,
                valueSnapshots: [],
                durableValueChanges: []);

            yield return new RuntimeSchedulerWorkItem(
                workItemId: RuntimeChainId.Derive(invokeWorkItem.WorkItemId, $"create-bookmark:{request.BookmarkId}"),
                workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                commandId: RuntimeChainId.Derive(invokeWorkItem.CommandId, $"create-bookmark:{request.BookmarkId}"),
                commandKind: WorkflowExecutionCommandKind.CreateBookmark,
                envelopeId: invokeWorkItem.EnvelopeId,
                idempotencyKey: RuntimeChainId.Derive(invokeWorkItem.IdempotencyKey, $"create-bookmark:{request.BookmarkId}"),
                enqueuedAt: now,
                recordedAt: now,
                sequence: invokeWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: MergeMetadata(invokeWorkItem.CommandMetadata, request),
                envelopeMetadata: invokeWorkItem.EnvelopeMetadata);
        }
    }

    private async ValueTask CommitChildSchedulingActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        IReadOnlyCollection<RuntimeSchedulerWorkItem> parentNotifications,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{invokeWorkItem.WorkItemId}:activity-inspection-captured:{invokePayload.ActivityExecutionId}";
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = invokeWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = invokeWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = "ChildActivityScheduling",
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = invokePayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = invokePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = invokePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = invokePayload.PinnedExecutable.ArtifactHash
        };
        var inspectionChanges = inspectionAccumulator is null
            ? []
            : new RuntimeStateChange<ActivityExecutionInspectionProjection>[]
            {
                new(
                    StateId: invokePayload.ActivityExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: await inspectionAccumulator.BuildProjectionAsync(
                        state,
                        checkpointId,
                        occurredAt,
                        valueSnapshots: valueSnapshots,
                        metadata: metadata,
                        cancellationToken: cancellationToken),
                    Metadata: metadata)
            };
        var childWorkItems = NewChildActivityScheduleWorkItems(idGenerator, invokeWorkItem, invokePayload, scheduleRequests).ToArray();
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{invokeWorkItem.WorkItemId}:activity-inspection-captured:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [invokePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: state,
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: durableValueChanges,
                incidents: [],
                operational: [],
                activityExecutionInspections: inspectionChanges),
            PostCommitIntents: childWorkItems
                .Concat(parentNotifications)
                .Select(workItem => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(invokeWorkItem, invokePayload.ActivityExecutionId, workItem, occurredAt))
                .ToArray(),
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private async ValueTask EnqueueChildActivityScheduleWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        CancellationToken cancellationToken)
    {
        foreach (var workItem in NewChildActivityScheduleWorkItems(idGenerator, invokeWorkItem, invokePayload, scheduleRequests))
            await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private IEnumerable<RuntimeSchedulerWorkItem> NewChildActivityScheduleWorkItems(
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests)
    {
        var requests = scheduleRequests.ToArray();
        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            var now = _timeProvider.GetUtcNow();
            var childActivityExecutionId = idGenerator.NewActivityExecutionId();
            var payload = new RuntimeScheduleActivityCommandPayload(
                invokePayload.PinnedExecutable,
                request.ExecutableNodeId,
                childActivityExecutionId,
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                request.SchedulingActivityExecutionId ?? invokePayload.ActivityExecutionId,
                invokePayload.ActivityExecutionId,
                request.SchedulingProvenance == ActivitySchedulingProvenance.Empty
                    ? ActivitySchedulingProvenance.From(
                        invokeWorkItem.WorkflowExecutionId,
                        invokePayload.ActivityExecutionId,
                        request.SchedulingActivityExecutionId ?? invokePayload.ActivityExecutionId,
                        branchId: null,
                        iterationId: null,
                        executionPathId: null,
                        executionScopeId: null,
                        schedulingCause: RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                        metadata: request.Metadata)
                    : request.SchedulingProvenance,
                request.IterationFrame);

            var commandMetadata = invokeWorkItem.CommandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            foreach (var item in request.Metadata)
                commandMetadata[item.Key] = item.Value;

            commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = invokePayload.ActivityExecutionId;
            commandMetadata[RuntimeMetadataKeys.ChildExecutableNodeId] = request.ExecutableNodeId;

            var workItem = new RuntimeSchedulerWorkItem(
                workItemId: RuntimeChainId.Derive(invokeWorkItem.WorkItemId, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                commandId: RuntimeChainId.Derive(invokeWorkItem.CommandId, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                envelopeId: invokeWorkItem.EnvelopeId,
                idempotencyKey: RuntimeChainId.Derive(invokeWorkItem.IdempotencyKey, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                enqueuedAt: now,
                recordedAt: now,
                sequence: invokeWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: commandMetadata,
                envelopeMetadata: invokeWorkItem.EnvelopeMetadata);

            yield return workItem;
        }
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> commandMetadata,
        ActivityBookmarkRequest request)
    {
        var metadata = commandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in request.Metadata)
            metadata[item.Key] = item.Value;

        metadata[RuntimeMetadataKeys.BookmarkId] = request.BookmarkId;
        metadata[RuntimeMetadataKeys.ResumeTargetId] = request.ResumeTargetId;
        metadata[RuntimeMetadataKeys.StimulusType] = request.StimulusType;
        metadata[RuntimeMetadataKeys.StimulusHash] = request.StimulusHash;
        return metadata;
    }

    /// <summary>
    /// Discrete completion commit (spec 123 D2 seam): builds the intent-free <c>ActivityCompleted</c> commit core and
    /// re-attaches the <c>CompleteActivity</c> continuation post-commit intent (ahead of any staged workflow-dispatch
    /// start intent, preserving today's ordering), reproducing the commit byte-for-byte. The commit builder and the
    /// derived <c>CompleteActivity</c> work item are extracted into <see cref="BuildCompletedCommitAsync"/> so a future
    /// fused-completion driver (D2) can commit the same stage without the completion enqueue intent and run the parent
    /// completion cascade inline instead of enqueuing it. This is the only invoke-handler surgery in spec 123 and is
    /// strictly behavior-preserving.
    /// </summary>
    private async ValueTask CommitCompletedActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState,
        IReadOnlyCollection<string> outcomeNames,
        IReadOnlyCollection<RuntimeSchedulerWorkItem> parentNotifications,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        WorkflowDispatchCheckpointRequest? workflowDispatch,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var core = await BuildCompletedCommitAsync(
            inspectionAccumulator,
            invokeWorkItem,
            invokePayload,
            completedState,
            outcomeNames,
            valueSnapshots,
            durableValueChanges,
            workflowDispatch,
            occurredAt,
            cancellationToken);

        var completionIntent = SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
            invokeWorkItem, invokePayload.ActivityExecutionId, core.CompletionWorkItem, occurredAt);
        // spec 126 seam C: a structural child completing on its initial execution may also notify its parent;
        // the notification intents ride behind the completion cascade intent and ahead of any staged dispatch.
        var notificationIntents = parentNotifications
            .Select(workItem => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(invokeWorkItem, invokePayload.ActivityExecutionId, workItem, occurredAt))
            .ToArray();
        var commit = core.Commit with
        {
            PostCommitIntents = [completionIntent, .. notificationIntents, .. core.Commit.PostCommitIntents]
        };

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>
    /// The <c>ActivityCompleted</c> stage core (spec 123 D2 seam): produces the <c>ActivityCompleted</c> checkpoint
    /// commit <b>without</b> its <c>CompleteActivity</c> continuation post-commit intent, alongside the derived
    /// <c>CompleteActivity</c> work item and the checkpoint's occurrence time. Any staged workflow-dispatch start intent
    /// remains on the commit (it is a separate dispatch, not the completion cascade). The discrete handler re-attaches
    /// the completion intent (<see cref="CommitCompletedActivityAsync"/>); a fused-completion driver commits the
    /// intent-free commit and runs the parent completion cascade inline. Reused by both — never re-implemented — so the
    /// two paths stay byte-identical by construction.
    /// </summary>
    private async ValueTask<CompletedCommitCore> BuildCompletedCommitAsync(
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState,
        IReadOnlyCollection<string> outcomeNames,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        WorkflowDispatchCheckpointRequest? workflowDispatch,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var checkpointId = $"checkpoint:{invokeWorkItem.WorkItemId}:activity-completed:{invokePayload.ActivityExecutionId}";
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = invokeWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = invokeWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = invokePayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = invokePayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = invokePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = invokePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = invokePayload.PinnedExecutable.ArtifactHash
        };
        var inspection = inspectionAccumulator is null
            ? null
            : await inspectionAccumulator.BuildProjectionAsync(
                completedState,
                checkpointId,
                occurredAt,
                outcomeNames: outcomeNames,
                valueSnapshots: valueSnapshots,
                metadata: metadata,
                cancellationToken: cancellationToken);
        var completionWorkItem = NewCompletionWorkItem(invokeWorkItem, invokePayload, completedState);
        RuntimeStateChange<WorkflowDispatchRecord>[] workflowDispatches = workflowDispatch is null
            ? []
            :
            [
                new RuntimeStateChange<WorkflowDispatchRecord>(
                    workflowDispatch.Record.DispatchId,
                    RuntimeStateChangeOperation.Upsert,
                    workflowDispatch.Record,
                    metadata)
            ];
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{invokeWorkItem.WorkItemId}:activity-completed:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCompleted,
                WorkflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [invokePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: completedState,
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: durableValueChanges,
                incidents: [],
                operational: [],
                workflowDispatches: workflowDispatches,
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: invokePayload.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata)
                    ]),
            PostCommitIntents: workflowDispatch is null
                ? []
                : [workflowDispatch.StartIntent],
            Metadata: metadata);

        return new CompletedCommitCore(commit, completionWorkItem, occurredAt);
    }

    /// <summary>
    /// The intent-free <c>ActivityCompleted</c> commit plus the derived <c>CompleteActivity</c> continuation work item
    /// and the checkpoint's occurrence time (spec 123 D2 seam).
    /// </summary>
    private readonly record struct CompletedCommitCore(
        RuntimeCheckpointCommit Commit,
        RuntimeSchedulerWorkItem CompletionWorkItem,
        DateTimeOffset OccurredAt);

    private static Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration AssertSingleDispatchRegistration(
        ActivityExecutionState suspendedState,
        ActivityBookmarkRequest bookmark)
    {
        // The registration's ResumeTargetKey is the node-scoped id resolved by the suspension projector;
        // the staged wait bookmark still carries the activity's local id, so match on the wait identity
        // (stimulus type + hash) and let the registration supply the authoritative resume-target id.
        var matches = (suspendedState.TriggerRegistrations ?? [])
            .Where(registration =>
                StringComparer.Ordinal.Equals(registration.StimulusType, bookmark.StimulusType) &&
                StringComparer.Ordinal.Equals(registration.StimulusHash, bookmark.StimulusHash))
            .ToArray();
        if (matches.Length != 1 || suspendedState.TriggerRegistrations?.Count != 1)
            throw new InvalidOperationException("A waited workflow dispatch must suspend with exactly one matching typed trigger registration.");

        return matches[0];
    }

    private RuntimeSchedulerWorkItem NewCompletionWorkItem(
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeCompleteActivityCommandPayload(
            invokePayload.PinnedExecutable,
            invokePayload.ExecutableNodeId,
            invokePayload.ActivityExecutionId,
            completedState.ParentActivityExecutionId,
            completedState.BranchId,
            ReadCompletionOutcomeNames(completedState),
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(invokeWorkItem.WorkItemId, $"complete:{invokePayload.ActivityExecutionId}"),
            workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(invokeWorkItem.CommandId, $"complete:{invokePayload.ActivityExecutionId}"),
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: invokeWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(invokeWorkItem.IdempotencyKey, $"complete:{invokePayload.ActivityExecutionId}"),
            enqueuedAt: now,
            recordedAt: now,
            sequence: invokeWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: invokeWorkItem.CommandMetadata,
            envelopeMetadata: invokeWorkItem.EnvelopeMetadata);
    }

    private static RuntimeInvokeActivityCommandPayload DeserializeInvokePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "InvokeActivity scheduler work item requires an invoke activity payload.",
            resolvedToNullMessage: "InvokeActivity scheduler work item payload resolved to null.",
            invalidPayloadMessage: "InvokeActivity scheduler work item payload is not a valid invoke activity payload.",
            deserialize: static (_, payload) => payload.Deserialize<RuntimeInvokeActivityCommandPayload>(),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException ||
                exception is ArgumentException argumentException && IsInvokePayloadValidationException(argumentException));

    private static bool IsInvokePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason";

    private ActivityExecutionState CompleteActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        IReadOnlyCollection<string> outcomeNames,
        bool skipped)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.InvokeReason] = invokePayload.Reason;
        metadata[RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(outcomeNames);

        if (skipped)
            metadata[RuntimeMetadataKeys.InvokeSkipped] = bool.TrueString;

        return RuntimeContainerScopeService.CloseOwnedFrames(state with
        {
            Status = ActivityExecutionStatus.Completed,
            SubStatus = skipped ? SkippedSubStatus : null,
            CompletedAt = _timeProvider.GetUtcNow(),
            PrivateState = null,
            Metadata = metadata
        });
    }

    private static IReadOnlyCollection<string> ReadCompletionOutcomeNames(ActivityExecutionState completedState)
    {
        if (completedState.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(outcomeNames, defaultToDone: false);
        }

        return completedState.SubStatus == SkippedSubStatus ? [] : [ActivityOutcomes.Done];
    }

    private static async ValueTask<IReadOnlyDictionary<string, object?>> MaterializeCheckpointInputsAsync(
        ActivityInputSnapshot snapshot,
        IExternalPayloadStore? externalPayloadStore,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, envelope) in snapshot.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (envelope.Presence)
            {
                case ValuePresence.Absent:
                    continue;
                case ValuePresence.ExplicitNull:
                    result.Add(key, null);
                    break;
                case ValuePresence.Present when envelope.InlineValue is { } inlineValue:
                    result.Add(key, inlineValue.Clone());
                    break;
                case ValuePresence.Present when envelope.ExternalReference is { } externalReference:
                    if (externalPayloadStore is null)
                        throw new InvalidOperationException($"Activity input '{key}' requires an IExternalPayloadStore for checkpoint participation.");
                    result.Add(key, await externalPayloadStore.ReadAsync(externalReference, cancellationToken));
                    break;
                default:
                    throw new InvalidOperationException($"Activity input '{key}' has an invalid value envelope.");
            }
        }

        return result;
    }

    private static IReadOnlyCollection<DurableValueState> ApplyDurableValueChanges(
        IReadOnlyCollection<DurableValueState> persisted,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> changes)
    {
        var values = persisted.ToDictionary(value => value.DurableValueId, StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (change.Operation == RuntimeStateChangeOperation.Delete)
                values.Remove(change.StateId);
            else
                values[change.StateId] = change.State;
        }

        return values.Values.ToArray();
    }

    private static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> MergeDurableValueChanges(
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> entryChanges,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> completionChanges)
    {
        var changes = entryChanges.ToDictionary(change => change.StateId, StringComparer.Ordinal);
        foreach (var change in completionChanges)
            changes[change.StateId] = change;
        return changes.Values.ToArray();
    }

}

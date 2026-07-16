using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
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
        var workflowExecutableStore = serviceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var activityExecutionStateStore = serviceProvider.GetRequiredService<IActivityExecutionStateStore>();
        var schedulerWorkQueue = serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

        var executable = await workflowExecutableStore.FindAsync(invokePayload.PinnedExecutable.ArtifactId, cancellationToken);
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
        {
            await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, invokePayload, state, cancellationToken);
            return;
        }

        if (state.Status != ActivityExecutionStatus.Running)
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
        var scopeService = new RuntimeContainerScopeService(
            activityExecutionStateStore,
            serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>());

        RuntimeInputBindingStateProjectionSet projections;
        IReadOnlyDictionary<string, object?> workflowInputValues;
        IReadOnlyDictionary<string, object?> activityOutputValues;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges = [];

        // Carrier identity (ADR 0030: correlation id / instance name) is projected from the IdentityName-tagged durable
        // values this invocation already re-lists (spec 083 review), so a plain activity populates the carrier without
        // a per-invocation workflow-execution-state read. A Correlate/SetName leaf writes the new value as a durable
        // value (see the identity fold below), which every activity invocation — including a concurrent sibling
        // branch — re-lists, so cross-branch visibility holds. The control-leaf state change below is the only path
        // that loads the workflow-execution state, and only when an intent actually mutates it.
        try
        {
            var durableValues = await durableValueStateStore.ListAsync(workItem.WorkflowExecutionId, cancellationToken);
            projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
            workflowInputValues = projections.WorkflowInputs;
            activityOutputValues = projections.ActivityOutputValues;

            if (executableNode.ActivityContract is null)
                throw new InvalidOperationException($"VF-ACT-001: Executable CLR activity node '{executableNode.ExecutableNodeId}' has no pinned activity contract.");

            state.EnsureValueFlowCompatible();
            if (state.InputSnapshot is null)
                throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{state.InvocationId}' has no committed input snapshot.");
        }
        catch (OperationCanceledException)
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
            valueFlowAttempt = state.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null);
            if (valueFlowAttempt is null)
            {
                if (state.Completion is not null)
                    throw new InvalidOperationException($"VF-ACT-007: Completed activity invocation '{state.InvocationId}' cannot create another attempt.");

                var attempts = state.Attempts?.ToArray() ?? [];
                var ordinal = attempts.Length == 0 ? 1 : attempts.Max(attempt => attempt.Ordinal) + 1;
                valueFlowAttempt = new ActivityAttempt(
                    $"{state.InvocationId}:attempt:{ordinal}",
                    state.InvocationId,
                    ordinal,
                    ActivityAttemptReason.Retry,
                    _timeProvider.GetUtcNow());
                state = state with { Attempts = attempts.Append(valueFlowAttempt).ToArray() };
            }
            activationLease = await serviceProvider.GetRequiredService<IActivityActivator>().ActivateAsync(
                new ActivityActivationRequest(activityContract, valueFlowSnapshot, valueFlowAttempt),
                cancellationToken);
            activity = activationLease.Activity;

            activity.NodeId = executableNode.ExecutableNodeId;
            activity.Id = invokePayload.ActivityExecutionId;

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
                triggerNodeId: projections.TriggerNodeId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFaultAsync(activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, workItem, invokePayload, state, exception, "ActivityConstructionFailed", valueSnapshots, cancellationToken);
            return;
        }

        ActivityExecutionState? completedState = null;
        (IRuntimeExecutionIdGenerator IdGenerator, IReadOnlyCollection<RuntimeChildActivityScheduleRequest> Requests)? pendingChildScheduling = null;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> pendingChildSchedulingDurableValueChanges = [];
        var finishWorkflowRequested = false;
        IReadOnlyCollection<string> finishWorkflowOutcomeNames = [];
        var correlationIdAssignmentRequested = false;
        string? requestedCorrelationId = null;
        var instanceNameAssignmentRequested = false;
        string? requestedInstanceName = null;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> workflowOutputChanges = [];
        ActivityTransition? returnedTransition = null;
        ActivityCompletionProjection? valueFlowCompletion = null;
        ActivityExecutionState? typedSuspendedState = null;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> typedSuspensionDurableValueChanges = [];
        try
        {
            if (!await activity.CanExecuteAsync(context))
            {
                completedState = CompleteActivity(workItem, invokePayload, state, outcomeNames: [], skipped: true);
            }
            else
            {
                returnedTransition = await activity.ExecuteAsync(context);

                // Control-leaf intents (Finish/Complete, Correlate, SetName, SetOutput): captured here and
                // drained below so the engine ends the run or persists the new correlation id / instance name /
                // workflow output rather than the activity having to reach into workflow-level state directly.
                finishWorkflowRequested = context.FinishWorkflowRequested;
                finishWorkflowOutcomeNames = context.FinishWorkflowOutcomeNames;
                correlationIdAssignmentRequested = context.CorrelationIdAssignmentRequested;
                requestedCorrelationId = context.RequestedCorrelationId;
                instanceNameAssignmentRequested = context.InstanceNameAssignmentRequested;
                requestedInstanceName = context.RequestedInstanceName;

                // SetOutput folds OutputName-tagged durable values into the activity's durable-value change set
                // (the same durable/output channel activity outputs use), so the named workflow output is
                // durably persisted on the activity's checkpoint boundary, like the workflow-variable write-back.
                workflowOutputChanges = context.WorkflowOutputAssignmentRequested
                    ? RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges(workItem.WorkflowExecutionId, context.RequestedWorkflowOutputs, _timeProvider.GetUtcNow())
                    : [];

                var childScheduleRequests = context.GetChildActivityScheduleRequests();
                if (context.CompositeCompletionRequested && childScheduleRequests.Count > 0)
                    throw new InvalidOperationException("Activity cannot both request composite completion and schedule child activities in the same execution.");

                if (finishWorkflowRequested && childScheduleRequests.Count > 0)
                    throw new InvalidOperationException("Activity cannot both request workflow completion and schedule child activities in the same execution.");

                // SetOutput values produced before suspension/hand-off are folded into the continuation checkpoint.
                var suspendDurableValueChanges = workflowOutputChanges;

                if (returnedTransition is IStatefulActivitySuspensionTransition statefulSuspension)
                {
                    if (valueFlowAttempt is null)
                        throw new InvalidOperationException("A stateful suspension requires a pinned typed activity contract and active attempt.");

                    if (childScheduleRequests.Count > 0 || context.CompositeCompletionRequested || finishWorkflowRequested)
                        throw new InvalidOperationException("A stateful suspension transition cannot also request child scheduling, composite completion, or workflow completion.");

                    ValidateStatefulSuspensionRegistrations(executable, executableNode, statefulSuspension);
                    typedSuspendedState = StatefulActivitySuspensionProjector.Project(
                        state,
                        valueFlowAttempt,
                        statefulSuspension,
                        _timeProvider.GetUtcNow());
                    typedSuspensionDurableValueChanges = suspendDurableValueChanges;
                }
                else if (returnedTransition is IActivityFaultTransition faultTransition)
                {
                    var fault = faultTransition.Fault;
                    var faultedState = state with
                    {
                        Fault = new NormalizedActivityFault(
                            fault.Code,
                            typeof(ActivityFault).FullName!,
                            fault.Message,
                            sanitizedStackTrace: null,
                            fault.IsRetryable)
                    };
                    await RecordFaultAsync(
                        activityFaultIncidentRecorder,
                        activityExecutionStateStore,
                        checkpointCommitter,
                        workItem,
                        invokePayload,
                        faultedState,
                        new ActivityTransitionFaultException(fault),
                        "ActivityReturnedFault",
                        valueSnapshots,
                        cancellationToken);
                    return;
                }
                else if (returnedTransition is IActivityCancellationTransition cancellationTransition)
                {
                    await ActivityCancellationCheckpointService.CommitAsync(
                        checkpointCommitter,
                        inspectionAccumulator,
                        _timeProvider,
                        workItem,
                        state,
                        cancellationTransition.Reason,
                        valueSnapshots,
                        cancellationToken: cancellationToken);
                    return;
                }
                else if (childScheduleRequests.Count > 0)
                {
                    var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();
                    if (inspectionAccumulator is null)
                    {
                        // No checkpoint on this path (the child work is enqueued directly), so flush the write-back
                        // here. This mirrors the completion non-inspection path, which likewise saves durable
                        // values then enqueues sequentially — there is no transactional unit to fold into.
                        await SaveDurableValueChangesAsync(durableValueStateStore, suspendDurableValueChanges, cancellationToken);
                        await EnqueueChildActivityScheduleWorkAsync(schedulerWorkQueue, idGenerator, workItem, invokePayload, childScheduleRequests, cancellationToken);
                        return;
                    }

                    // The child-scheduling checkpoint commits the write-back in its durable-value change set below.
                    pendingChildSchedulingDurableValueChanges = suspendDurableValueChanges;
                    pendingChildScheduling = (idGenerator, childScheduleRequests);
                }
                else
                {
                    var recordedOutputs = await ProjectReturnedCompletionAsync(executableNode.ActivityContract!);
                    if (recordedOutputs.Count > 0)
                    {
                        var recordedAt = _timeProvider.GetUtcNow();
                        valueSnapshots.AddRange(ActivityExecutionInspection.BuildOutputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, executableNode, recordedOutputs, recordedAt));
                    }

                    var outcomeNames = valueFlowCompletion is not null
                        ? [valueFlowCompletion.Completion.OutcomeKey]
                        : context.CompositeCompletionRequested
                            ? SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(context.CompositeCompletionOutcomeNames, defaultToDone: true)
                            : finishWorkflowRequested
                                ? SchedulerWorkHandlerHelpers.NormalizeOutcomeNames(finishWorkflowOutcomeNames, defaultToDone: true)
                                : [ActivityOutcomes.Done];
                    completedState = CompleteActivity(workItem, invokePayload, state, outcomeNames, skipped: false);
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
                        var priorAttempts = state.Attempts?.Where(attempt => attempt.AttemptId != completedAttempt.AttemptId) ?? [];
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
                    if (context.CompositeCompletionRequested)
                    {
                        var containerVariableSnapshots = RuntimeContainerVariableEvidence.Capture(
                            payloadCapturePolicy, scopeService, executableNode, state,
                            workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, workItem.WorkItemId, _timeProvider.GetUtcNow());
                        if (containerVariableSnapshots.Count > 0)
                        {
                            valueSnapshots.AddRange(containerVariableSnapshots);
                            completedState = RuntimeContainerScopeService.CloseOwnedFrames(completedState);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFaultAsync(activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, workItem, invokePayload, state, exception, "ActivityFaulted", valueSnapshots, cancellationToken);
            return;
        }
        finally
        {
            if (activationLease is not null)
                await activationLease.DisposeAsync();
        }

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
            return valueFlowCompletion.Projections
                .Where(item => item.Value.Presence != ValuePresence.Absent && item.Value.Policy.Storage != DurableValueStorage.External)
                .Select(item => new RecordedActivityOutput(
                    item.Key,
                    item.Value.Presence == ValuePresence.ExplicitNull ? null : item.Value.InlineValue))
                .ToArray();
        }

        if (typedSuspendedState is not null)
        {
            await CommitStatefulSuspensionAsync(
                checkpointCommitter,
                inspectionAccumulator,
                workItem,
                invokePayload,
                typedSuspendedState,
                valueSnapshots,
                typedSuspensionDurableValueChanges,
                cancellationToken);
            return;
        }

        if (pendingChildScheduling is { } childScheduling)
        {
            await CommitChildSchedulingActivityAsync(checkpointCommitter, inspectionAccumulator!, childScheduling.IdGenerator, workItem, invokePayload, state, childScheduling.Requests, valueSnapshots, pendingChildSchedulingDurableValueChanges, cancellationToken);
            return;
        }

        if (completedState is null)
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' did not produce a completion or child scheduling result for activity execution '{invokePayload.ActivityExecutionId}'.");

        // Fold SetOutput's OutputName-tagged durable values into the activity completion checkpoint.
        if (workflowOutputChanges.Count > 0)
            durableValueChanges = durableValueChanges.Concat(workflowOutputChanges).ToArray();

        var occurredAt = _timeProvider.GetUtcNow();

        // Fold a Correlate/SetName leaf's identity into the durable-value change set as an IdentityName-tagged
        // projection (spec 083 review), alongside SetOutput. Every activity invocation re-lists durable values, so a
        // concurrent sibling branch observes the new correlation id / instance name — the cross-branch visibility the
        // per-branch-lineage channel could not provide. This is an additional projection channel; the control-leaf
        // state change below keeps WorkflowExecutionState.CorrelationId / system-metadata InstanceName as the
        // authoritative queryable home, and both commit in the same activity-completed commit so they stay consistent.
        // A cleared assignment writes a JSON-null durable value so the clear propagates. NOTE: the suspend paths above
        // (bookmark-creation / child-scheduling) return before reaching here, so an activity that assigns identity AND
        // suspends drops the projection — consistent with the state change, which those paths also skip (pre-existing).
        if (correlationIdAssignmentRequested || instanceNameAssignmentRequested)
        {
            var identityChanges = RuntimeWorkflowStateSeed.BuildIdentityChanges(
                workItem.WorkflowExecutionId, correlationIdAssignmentRequested, requestedCorrelationId,
                instanceNameAssignmentRequested, requestedInstanceName, occurredAt);
            durableValueChanges = durableValueChanges.Concat(identityChanges).ToArray();
        }

        // Resolve a workflow-execution state change requested by a control-leaf intent (Finish ends the run;
        // Correlate updates the correlation id; SetName updates the instance name). All fold into the same
        // activity-completed commit so the workflow state is persisted atomically with the activity completion. The
        // state is loaded lazily here — only when an intent mutates it — so a plain activity pays no state read.
        var workflowExecutionStateChange = await BuildControlLeafWorkflowExecutionStateChangeAsync(
            serviceProvider, workItem, finishWorkflowRequested, correlationIdAssignmentRequested, requestedCorrelationId,
            instanceNameAssignmentRequested, requestedInstanceName, occurredAt, cancellationToken);

        if (inspectionAccumulator is null)
        {
            foreach (var change in durableValueChanges)
            {
                if (change.Operation != RuntimeStateChangeOperation.Upsert || change.State is null)
                    throw new InvalidOperationException($"Unsupported durable value change '{change.Operation}' while completing activity without checkpoint inspection.");

                await durableValueStateStore.SaveAsync(change.State, cancellationToken);
            }

            if (workflowExecutionStateChange is not null)
                await serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(workflowExecutionStateChange.State, cancellationToken);

            await activityExecutionStateStore.SaveAsync(completedState, cancellationToken);

            // A Finish leaf ends the whole run: the activity is recorded and the workflow state is marked
            // completed above, but no further completion-propagation work is scheduled.
            if (!finishWorkflowRequested)
                await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, invokePayload, completedState, cancellationToken);
            return;
        }

        await CommitCompletedActivityAsync(checkpointCommitter, inspectionAccumulator, workItem, invokePayload, completedState, ReadCompletionOutcomeNames(completedState), valueSnapshots, durableValueChanges, workflowExecutionStateChange, finishWorkflowRequested, occurredAt, cancellationToken);
    }

    // Records a blocking fault incident for the activity and commits it. Each fault arm in InvokeActivityAsync
    // (input materialization, construction/binding, execution) differs only in its reason and snapshot set;
    // centralizing the request shape + commit here keeps those arms to one call. When the faulted activity has a
    // parent fork/join, it also rides a child-fault parent-evaluation work item along on the incident checkpoint so
    // the parent can resolve its join deterministically (#308) instead of waiting forever for a completion that
    // never arrives. Parents that do not implement IActivityChildFaultHandler no-op on that work item, so the fault
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

    // Persists durable-value upserts directly to the store, used on the non-inspection child-scheduling path that
    // enqueues continuation work without a checkpoint. Empty input is a no-op, so the dirty-tracked workflow-variable
    // write-back writes nothing unless a variable actually changed.
    private static async ValueTask SaveDurableValueChangesAsync(
        IDurableValueStateStore durableValueStateStore,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> changes,
        CancellationToken cancellationToken)
    {
        foreach (var change in changes)
        {
            if (change.Operation != RuntimeStateChangeOperation.Upsert || change.State is null)
                throw new InvalidOperationException($"Unsupported durable value change '{change.Operation}' while persisting workflow-scope variable write-back.");

            await durableValueStateStore.SaveAsync(change.State, cancellationToken);
        }
    }

    // Resolves the workflow-execution state change requested by a control-leaf intent (Finish/Correlate/SetName),
    // loading the workflow-execution state only when an intent is actually present. This lazy guard is what keeps a
    // plain activity invocation — the common case — free of any workflow-execution-state read (spec 083 follow-up):
    // the carrier reads identity from the durable-value projection instead, and only a mutating leaf pays the load.
    private static async ValueTask<RuntimeStateChange<WorkflowExecutionState>?> BuildControlLeafWorkflowExecutionStateChangeAsync(
        IServiceProvider serviceProvider,
        RuntimeSchedulerWorkItem workItem,
        bool finishWorkflowRequested,
        bool correlationIdAssignmentRequested,
        string? requestedCorrelationId,
        bool instanceNameAssignmentRequested,
        string? requestedInstanceName,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (!finishWorkflowRequested && !correlationIdAssignmentRequested && !instanceNameAssignmentRequested)
            return null;

        var workflowExecutionStateStore = serviceProvider.GetService<IWorkflowExecutionStateStore>();
        var workflowState = workflowExecutionStateStore is null
            ? null
            : await workflowExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
        if (workflowState is null)
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' references missing workflow execution '{workItem.WorkflowExecutionId}'.");

        var updatedState = workflowState with
        {
            CorrelationId = correlationIdAssignmentRequested ? requestedCorrelationId : workflowState.CorrelationId,
            Status = finishWorkflowRequested ? WorkflowExecutionStatus.Completed : workflowState.Status,
            SubStatus = finishWorkflowRequested ? null : workflowState.SubStatus,
            CompletedAt = finishWorkflowRequested ? occurredAt : workflowState.CompletedAt,
            UpdatedAt = occurredAt,
            SystemMetadata = instanceNameAssignmentRequested
                ? ApplyInstanceName(workflowState.SystemMetadata, requestedInstanceName)
                : workflowState.SystemMetadata
        };

        if (finishWorkflowRequested)
            updatedState = RuntimeContainerScopeService.CloseRootFrame(updatedState);

        return new RuntimeStateChange<WorkflowExecutionState>(
            StateId: updatedState.WorkflowExecutionId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: updatedState,
            Metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CheckpointReason] = finishWorkflowRequested
                    ? "WorkflowFinish"
                    : correlationIdAssignmentRequested ? "WorkflowCorrelation" : "WorkflowName"
            });
    }

    // Returns the workflow's system metadata with the instance-name key set (or removed when the name is
    // cleared). Used by SetName (#260) to fold the instance name into the workflow-execution state change.
    private static IReadOnlyDictionary<string, string> ApplyInstanceName(IReadOnlyDictionary<string, string> systemMetadata, string? instanceName)
    {
        var metadata = systemMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(instanceName))
            metadata.Remove(RuntimeMetadataKeys.InstanceName);
        else
            metadata[RuntimeMetadataKeys.InstanceName] = instanceName;

        return metadata;
    }

    private static void ValidateStatefulSuspensionRegistrations(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        IStatefulActivitySuspensionTransition suspension)
    {
        foreach (var registration in suspension.Registrations)
        {
            if (!executable.ResumeTargets.TryGetValue(registration.ResumeTargetKey, out var resumeTarget))
            {
                throw new InvalidOperationException(
                    $"Stateful activity '{executableNode.ExecutableNodeId}' registered missing resume target '{registration.ResumeTargetKey}'.");
            }

            if (!StringComparer.Ordinal.Equals(resumeTarget.ExecutableNodeId, executableNode.ExecutableNodeId))
            {
                throw new InvalidOperationException(
                    $"Resume target '{registration.ResumeTargetKey}' belongs to executable node '{resumeTarget.ExecutableNodeId}', not '{executableNode.ExecutableNodeId}'.");
            }
        }
    }

    private async ValueTask CommitStatefulSuspensionAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState suspendedState,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
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
        var bookmarkWorkItems = NewBookmarkCreationWorkItems(
                invokeWorkItem,
                invokePayload,
                bookmarkRequests,
                valueSnapshots: [],
                durableValueChanges: [])
            .ToArray();
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{invokeWorkItem.WorkItemId}:activity-suspended:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivitySuspended,
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
                bookmarks: [],
                durableValues: durableValueChanges,
                incidents: [],
                operational: [],
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
                .ToArray(),
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private IEnumerable<RuntimeSchedulerWorkItem> NewBookmarkCreationWorkItems(
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<ActivityBookmarkRequest> bookmarkRequests,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges)
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
                valueSnapshots: index == 0 ? valueSnapshots : [],
                // Carry the suspend-path write-back on the first bookmark only; the downstream handler commits it
                // atomically in that bookmark-created checkpoint (#310). The changes are idempotent upserts.
                durableValueChanges: index == 0 ? durableValueChanges : []);

            yield return new RuntimeSchedulerWorkItem(
                workItemId: $"{invokeWorkItem.WorkItemId}:create-bookmark:{request.BookmarkId}",
                workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                commandId: $"{invokeWorkItem.CommandId}:create-bookmark:{request.BookmarkId}",
                commandKind: WorkflowExecutionCommandKind.CreateBookmark,
                envelopeId: invokeWorkItem.EnvelopeId,
                idempotencyKey: $"{invokeWorkItem.IdempotencyKey}:create-bookmark:{request.BookmarkId}",
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
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
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
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            state,
            checkpointId,
            occurredAt,
            valueSnapshots: valueSnapshots,
            metadata: metadata,
            cancellationToken: cancellationToken);
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
                activityExecutions: [],
                bookmarks: [],
                // The suspend-path write-back (#286/#260) commits in the same transactional unit as the
                // child-scheduling checkpoint (#310), so it is durable iff the continuation work is enqueued.
                durableValues: durableValueChanges,
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: childWorkItems
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
                workItemId: $"{invokeWorkItem.WorkItemId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                commandId: $"{invokeWorkItem.CommandId}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
                commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                envelopeId: invokeWorkItem.EnvelopeId,
                idempotencyKey: $"{invokeWorkItem.IdempotencyKey}:schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}",
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

    private async ValueTask EnqueueCompletionWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState,
        CancellationToken cancellationToken)
    {
        var workItem = NewCompletionWorkItem(invokeWorkItem, invokePayload, completedState);
        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private async ValueTask CommitCompletedActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState completedState,
        IReadOnlyCollection<string> outcomeNames,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        RuntimeStateChange<WorkflowExecutionState>? workflowExecutionStateChange,
        bool finishWorkflowRequested,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        // A Finish leaf records the activity completion and a terminal WorkflowCompleted checkpoint; no
        // completion-propagation work follows, because the whole run ends here.
        var checkpointName = finishWorkflowRequested ? RuntimeCheckpointNames.WorkflowCompleted : RuntimeCheckpointNames.ActivityCompleted;
        var checkpointId = $"checkpoint:{invokeWorkItem.WorkItemId}:{(finishWorkflowRequested ? "workflow-completed" : "activity-completed")}:{invokePayload.ActivityExecutionId}";
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
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            completedState,
            checkpointId,
            occurredAt,
            outcomeNames: outcomeNames,
            valueSnapshots: valueSnapshots,
            metadata: metadata,
            cancellationToken: cancellationToken);
        var completionWorkItem = NewCompletionWorkItem(invokeWorkItem, invokePayload, completedState);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{invokeWorkItem.WorkItemId}:{(finishWorkflowRequested ? "workflow-completed" : "activity-completed")}:{invokePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: checkpointName,
                WorkflowExecutionId: invokeWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [invokePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: workflowExecutionStateChange,
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
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: invokePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: finishWorkflowRequested
                ? []
                : [SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(invokeWorkItem, invokePayload.ActivityExecutionId, completionWorkItem, occurredAt)],
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
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
            workItemId: $"{invokeWorkItem.WorkItemId}:complete:{invokePayload.ActivityExecutionId}",
            workflowExecutionId: invokeWorkItem.WorkflowExecutionId,
            commandId: $"{invokeWorkItem.CommandId}:complete:{invokePayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: invokeWorkItem.EnvelopeId,
            idempotencyKey: $"{invokeWorkItem.IdempotencyKey}:complete:{invokePayload.ActivityExecutionId}",
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

}

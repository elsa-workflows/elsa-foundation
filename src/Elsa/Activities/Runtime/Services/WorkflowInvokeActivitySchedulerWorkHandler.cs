using System.Text.Json;
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

    private readonly IRuntimeActivityInputMaterializer _inputMaterializer;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the handler. RT-8: collapsed to a single primary constructor (the former ambient-services-accessor
    /// overload is gone — RT-7 replaced that AsyncLocal service locator with the explicit
    /// <see cref="IRuntimePipelineContext"/> workspace carrier).
    /// </summary>
    public WorkflowInvokeActivitySchedulerWorkHandler(
        IRuntimeActivityInputMaterializer inputMaterializer,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inputMaterializer);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _inputMaterializer = inputMaterializer;
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
        var activityFactory = serviceProvider.GetRequiredService<IActivityFactory>();
        var schedulerWorkQueue = serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

        var executable = await workflowExecutableStore.FindAsync(invokePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(invokePayload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, invokePayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(invokePayload.ExecutableNodeId, out var executableNode))
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' references executable node '{invokePayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

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

        var activityOutputRegister = serviceProvider.GetRequiredService<IRuntimeActivityOutputRegister>();
        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var checkpointCommitter = serviceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var activityFaultIncidentRecorder = serviceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var inspectionAccumulator = serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>();
        var payloadCapturePolicy = serviceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();
        await InvokeActivityAsync(serviceProvider, activityFactory, activityExecutionStateStore, schedulerWorkQueue, activityOutputRegister, durableValueStateStore, checkpointCommitter, activityFaultIncidentRecorder, inspectionAccumulator, payloadCapturePolicy, workItem, invokePayload, executable, executableNode, state, cancellationToken);
    }

    private async ValueTask InvokeActivityAsync(
        IServiceProvider serviceProvider,
        IActivityFactory activityFactory,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeActivityOutputRegister activityOutputRegister,
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
        var scopeService = new RuntimeContainerScopeService(activityExecutionStateStore);

        IReadOnlyList<RuntimeMaterializedActivityInput> inputs;
        VariableScope? variableScope;
        RuntimeInputBindingStateProjectionSet projections;
        IReadOnlyDictionary<string, object?> workflowVariables;
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
            workflowVariables = projections.WorkflowVariables;
            workflowInputValues = projections.WorkflowInputs;
            activityOutputValues = projections.ActivityOutputValues;

            // Build the scope with the workflow-scope variables anchored from the current durable-value
            // projection (#286), so workflow-scope reads see prior mutations and writes land in the workflow
            // scope for the post-execution write-back below.
            variableScope = await scopeService.BuildScopeAsync(executable, workItem.WorkflowExecutionId, state, cancellationToken, workflowVariables);

            var resolutionContext = new RuntimeInputBindingResolutionContext(
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: invokePayload.ActivityExecutionId,
                durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
                activityOutputs: activityOutputRegister,
                serviceProvider: serviceProvider,
                workflowVariables: workflowVariables,
                workflowInputs: workflowInputValues,
                activityOutputValues: activityOutputValues,
                variableScope: variableScope);
            inputs = await _inputMaterializer.MaterializeInputsAsync(executableNode, resolutionContext, cancellationToken);
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
        try
        {
            valueSnapshots.AddRange(ActivityOutputPublisher.BuildInputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, inputs, _timeProvider.GetUtcNow()));

            // Activity construction + argument binding (ActivityArgumentBinder, invoked inside Create) runs
            // inside the same fault boundary as input materialization (#317). Previously this step sat between
            // the two materialization try/catch blocks, so a binder/constructor throw (e.g. a typed-binding
            // InvalidOperationException) escaped to the scheduler loop and left the run silently at Running with
            // no incident. Recording it as a blocking incident faults the activity and surfaces a queryable cause.
            activity = await activityFactory.Create(
                executableNode.DescriptorType,
                executableNode.DescriptorPayload,
                inputs.ToDictionary(input => input.Name, input => input.Argument, StringComparer.OrdinalIgnoreCase),
                ActivityOutputPublisher.BuildOutputArguments(executableNode),
                cancellationToken);

            activity.NodeId = executableNode.ExecutableNodeId;
            activity.Id = invokePayload.ActivityExecutionId;

            var carrier = RuntimeExecutionExpressionCarrier.Create(projections, invokePayload.PinnedExecutable);
            // Build the execution-time context through the single carrier-bearing factory (ADR 0030): identity + the
            // durable-value projections for inputs/variables/outputs all come from the one ProjectAll above, so a
            // Correlate/SetName is visible to a concurrent sibling branch and no workflow-execution-state read is
            // paid. These feed the re-pointed JavaScript pre/post-processors via the passed IExpressionExecutionContext.
            // The resume and parent-completion handlers build it the same way.
            context = SimpleActivityExecutionContext.ForExecution(
                serviceProvider,
                activity,
                cancellationToken,
                workItem.WorkflowExecutionId,
                invokePayload.PinnedExecutable,
                workItem,
                executableNode,
                state,
                variableScope,
                carrier);
            RuntimeActivityInputMemory.Seed(context, inputs);
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
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> workflowVariableWriteBackChanges = [];
        (IRuntimeExecutionIdGenerator IdGenerator, IReadOnlyCollection<RuntimeChildActivityScheduleRequest> Requests)? pendingChildScheduling = null;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> pendingChildSchedulingDurableValueChanges = [];
        var finishWorkflowRequested = false;
        IReadOnlyCollection<string> finishWorkflowOutcomeNames = [];
        var correlationIdAssignmentRequested = false;
        string? requestedCorrelationId = null;
        var instanceNameAssignmentRequested = false;
        string? requestedInstanceName = null;
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> workflowOutputChanges = [];
        try
        {
            if (!await activity.CanExecuteAsync(context))
            {
                completedState = CompleteActivity(workItem, invokePayload, state, outcomeNames: [], skipped: true);
            }
            else
            {
                await activity.ExecuteAsync(context);

                // Write back the activity's variable mutations: container-scope assignments persist to their
                // owning execution snapshots so sibling branches and later activities observe them and resume
                // restores them (ADR 0027), and the returned workflow-scope changes (#286) are folded into the
                // activity completion below so they commit on the activity's checkpoint boundary rather than
                // out-of-band — making variable-driven While/Do loops terminate and SetVariable durable.
                // Dirty-tracked against the start-of-activity projection, so a read-only activity produces no
                // change. Shared with the resume path so both stay in lockstep.
                workflowVariableWriteBackChanges = await scopeService.PersistAndCaptureWorkflowScopeWriteBackAsync(
                    variableScope, executable, workItem.WorkflowExecutionId, workflowVariables, _timeProvider.GetUtcNow(), cancellationToken);

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

                var bookmarkRequests = context.GetBookmarkRequests();
                var childScheduleRequests = context.GetChildActivityScheduleRequests();
                if (context.CompositeCompletionRequested && childScheduleRequests.Count > 0)
                    throw new InvalidOperationException("Activity cannot both request composite completion and schedule child activities in the same execution.");

                if (finishWorkflowRequested && childScheduleRequests.Count > 0)
                    throw new InvalidOperationException("Activity cannot both request workflow completion and schedule child activities in the same execution.");

                if (bookmarkRequests.Count > 0 && childScheduleRequests.Count > 0)
                    throw new InvalidOperationException("Activity cannot both request durable bookmarks and schedule child activities in the same execution.");

                // The workflow-scope variable write-back (#286) and SetOutput durable values (#260) the activity
                // produced before suspending/handing off. Folded into the continuation's checkpoint below so they
                // commit atomically with it rather than out-of-band (#310). Empty unless the activity actually
                // mutated a variable / set an output.
                var suspendDurableValueChanges = CombineDurableValueChanges(workflowVariableWriteBackChanges, workflowOutputChanges);

                if (bookmarkRequests.Count > 0)
                {
                    // A suspending activity does not reach the completion checkpoint; carry any write-back on the
                    // bookmark work item so the downstream WorkflowCreateBookmarkSchedulerWorkHandler commits it
                    // atomically in the bookmark-created checkpoint (#310).
                    await EnqueueBookmarkCreationWorkAsync(schedulerWorkQueue, workItem, invokePayload, bookmarkRequests, valueSnapshots, suspendDurableValueChanges, cancellationToken);
                    return;
                }

                if (childScheduleRequests.Count > 0)
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
                    var recordedOutputs = context.GetRecordedOutputs();
                    if (recordedOutputs.Count > 0)
                    {
                        var recordedAt = _timeProvider.GetUtcNow();
                        ActivityOutputPublisher.PublishActivityOutputs(activityOutputRegister, workItem, invokePayload, executableNode, recordedOutputs, recordedAt);
                        valueSnapshots.AddRange(ActivityOutputPublisher.BuildOutputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, executableNode, recordedOutputs, recordedAt));
                        durableValueChanges = ActivityOutputPublisher.BuildDurableOutputChanges(workItem, invokePayload, executableNode, recordedOutputs, recordedAt);
                    }

                    var outcomeNames = context.CompositeCompletionRequested
                        ? NormalizeOutcomeNames(context.CompositeCompletionOutcomeNames, defaultToDone: true)
                        : finishWorkflowRequested
                            ? NormalizeOutcomeNames(finishWorkflowOutcomeNames, defaultToDone: true)
                            : NormalizeOutcomeNames(context.GetOutcomes(), defaultToDone: true);
                    completedState = CompleteActivity(workItem, invokePayload, state, outcomeNames, skipped: false);

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
                            completedState = RuntimeContainerScopeService.MarkScopeCompleted(completedState);
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
            valueSnapshots.AddRange(ActivityOutputPublisher.BuildOutputValueSnapshots(payloadCapturePolicy, workItem, invokePayload, executableNode, context.GetRecordedOutputs(), _timeProvider.GetUtcNow()));
            await RecordFaultAsync(activityFaultIncidentRecorder, activityExecutionStateStore, checkpointCommitter, workItem, invokePayload, state, exception, "ActivityFaulted", valueSnapshots, cancellationToken);
            return;
        }

        if (pendingChildScheduling is { } childScheduling)
        {
            await CommitChildSchedulingActivityAsync(checkpointCommitter, inspectionAccumulator!, childScheduling.IdGenerator, workItem, invokePayload, state, childScheduling.Requests, valueSnapshots, pendingChildSchedulingDurableValueChanges, cancellationToken);
            return;
        }

        if (completedState is null)
            throw new InvalidOperationException($"InvokeActivity scheduler work item '{workItem.WorkItemId}' did not produce a completion or child scheduling result for activity execution '{invokePayload.ActivityExecutionId}'.");

        // Fold the workflow-scope variable write-back (#286) into the activity's durable-value change set so it
        // commits atomically with the completion (checkpoint path) or in the same save sequence (non-inspection
        // path), rather than out-of-band. Dirty-tracked upstream, so this adds nothing for a read-only activity.
        if (workflowVariableWriteBackChanges.Count > 0)
            durableValueChanges = durableValueChanges.Concat(workflowVariableWriteBackChanges).ToArray();

        // Fold SetOutput's OutputName-tagged durable values (#260) into the same change set, alongside the
        // workflow-variable write-back, so the named workflow output commits on the activity's checkpoint.
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

    // Concatenates the workflow-scope variable write-back and the SetOutput durable-value changes into the single
    // set the suspend paths fold into their continuation checkpoint (#310). Returns either input untouched when
    // the other is empty so the common no-mutation case allocates nothing.
    private static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> CombineDurableValueChanges(
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> variableWriteBackChanges,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> outputChanges)
    {
        if (outputChanges.Count == 0)
            return variableWriteBackChanges;
        if (variableWriteBackChanges.Count == 0)
            return outputChanges;

        return variableWriteBackChanges.Concat(outputChanges).ToArray();
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
        var request = ActivityOutputPublisher.NewFaultIncidentRecordRequest(checkpointCommitter, workItem, invokePayload, state, exception, subStatus, valueSnapshots);
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

        var workflowState = await RuntimeExecutionExpressionCarrier.LoadWorkflowStateAsync(serviceProvider, workItem.WorkflowExecutionId, cancellationToken);
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

    private async ValueTask EnqueueBookmarkCreationWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem invokeWorkItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        IReadOnlyCollection<ActivityBookmarkRequest> bookmarkRequests,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        CancellationToken cancellationToken)
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

            var workItem = new RuntimeSchedulerWorkItem(
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

            await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
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
                .Select(workItem => NewEnqueueSchedulerWorkIntent(invokeWorkItem, invokePayload.ActivityExecutionId, workItem, occurredAt))
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
                    : request.SchedulingProvenance);

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
                : [NewEnqueueSchedulerWorkIntent(invokeWorkItem, invokePayload.ActivityExecutionId, completionWorkItem, occurredAt)],
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

    private static RuntimePostCommitIntent NewEnqueueSchedulerWorkIntent(
        RuntimeSchedulerWorkItem sourceWorkItem,
        string activityExecutionId,
        RuntimeSchedulerWorkItem schedulerWorkItem,
        DateTimeOffset recordedAt) =>
        new(
            intentId: $"{sourceWorkItem.WorkItemId}:post-commit:{schedulerWorkItem.WorkItemId}",
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: recordedAt,
            activityExecutionId: activityExecutionId,
            idempotencyKey: $"{sourceWorkItem.IdempotencyKey}:post-commit:{schedulerWorkItem.IdempotencyKey}",
            payload: JsonSerializer.SerializeToElement(schedulerWorkItem),
            metadata: sourceWorkItem.CommandMetadata);

    private static RuntimeInvokeActivityCommandPayload DeserializeInvokePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("InvokeActivity scheduler work item requires an invoke activity payload.");

        try
        {
            return payload.Deserialize<RuntimeInvokeActivityCommandPayload>()
                   ?? throw new InvalidOperationException("InvokeActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsInvokePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("InvokeActivity scheduler work item payload is not a valid invoke activity payload.", exception);
        }
    }

    private static bool IsInvokePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason";

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"InvokeActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

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

        return state with
        {
            Status = ActivityExecutionStatus.Completed,
            SubStatus = skipped ? SkippedSubStatus : null,
            CompletedAt = _timeProvider.GetUtcNow(),
            Metadata = metadata
        };
    }

    private static IReadOnlyCollection<string> ReadCompletionOutcomeNames(ActivityExecutionState completedState)
    {
        if (completedState.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return NormalizeOutcomeNames(outcomeNames, defaultToDone: false);
        }

        return completedState.SubStatus == SkippedSubStatus ? [] : [ActivityOutcomes.Done];
    }

    private static IReadOnlyCollection<string> NormalizeOutcomeNames(IEnumerable<string> outcomeNames, bool defaultToDone)
    {
        var snapshot = outcomeNames.ToArray();
        if (snapshot.Length == 0)
            return defaultToDone ? [ActivityOutcomes.Done] : [];

        if (snapshot.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Activity completion outcome names cannot contain blank values.");

        if (snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new InvalidOperationException("Activity completion outcome names cannot contain duplicates.");

        return snapshot;
    }

}

using System.Text.Json;
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
    public const string HandlerName = nameof(WorkflowParentActivityCompletionSchedulerWorkHandler);

    private readonly IRuntimeActivityInputMaterializer _inputMaterializer;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the handler. RT-8: collapsed to a single primary constructor (the former ambient-services-accessor
    /// overload is gone — RT-7 replaced that AsyncLocal service locator with the explicit
    /// <see cref="IRuntimePipelineContext"/> workspace carrier).
    /// </summary>
    public WorkflowParentActivityCompletionSchedulerWorkHandler(
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
        var schedulerWorkQueue = serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();

        var executable = await workflowExecutableStore.FindAsync(payload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(payload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, payload.PinnedExecutable, executable.Identity);

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

        if (parentState.Status != ActivityExecutionStatus.Running)
            return;

        var activityFactory = serviceProvider.GetRequiredService<IActivityFactory>();
        var activityOutputRegister = serviceProvider.GetRequiredService<IRuntimeActivityOutputRegister>();
        var durableValueStateStore = serviceProvider.GetRequiredService<IDurableValueStateStore>();
        var idGenerator = serviceProvider.GetRequiredService<IRuntimeExecutionIdGenerator>();
        var checkpointCommitter = serviceProvider.GetService<RuntimeCheckpointCommitter>();
        var inspectionAccumulator = serviceProvider.GetService<IRuntimeActivityExecutionInspectionAccumulator>();
        var activityFaultIncidentRecorder = serviceProvider.GetRequiredService<ActivityFaultIncidentRecorder>();
        var payloadCapturePolicy = serviceProvider.GetService<IRuntimePayloadCapturePolicy>() ?? new DefaultRuntimePayloadCapturePolicy();

        // One scope service serves both the self-owner scope built for the child-completion evaluation below
        // and the completed-scope evidence capture on the completion path (ADR 0027/0030).
        var scopeService = new RuntimeContainerScopeService(activityExecutionStateStore);

        SimpleActivityExecutionContext context;
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots = [];
        try
        {
            var constructedParent = await ConstructActivityAsync(
                serviceProvider,
                activityFactory,
                activityOutputRegister,
                durableValueStateStore,
                workItem,
                payload,
                parentExecutableNode,
                cancellationToken);
            valueSnapshots = BuildInputValueSnapshots(payloadCapturePolicy, workItem, payload, constructedParent.Inputs, _timeProvider.GetUtcNow());
            var parentActivity = constructedParent.Activity;
            var childFaulted = IsChildFaulted(workItem);

            if (childFaulted)
            {
                // A faulted child is propagated only to parents that opt into child-fault handling (fork/join
                // composites). For any other parent the fault stays a blocking incident — sequential containers
                // must halt on a faulted step, not advance past it.
                if (parentActivity is not IActivityChildFaultHandler)
                    return;
            }
            else if (parentActivity is not IActivityChildCompletionHandler)
            {
                await EnqueueContinuationSchedulingAsync(schedulerWorkQueue, workItem, payload, cancellationToken);
                return;
            }

            // Load the workflow-execution state once, best-effort, for the execution-time expression carrier (ADR 0030
            // identity: correlation id / name / definition version) surfaced to a composite container's child-completion
            // logic. Absent state degrades identity to null rather than failing the evaluation. The load is deferred
            // past the handler-type gates above so non-composite parents don't pay for the FindAsync round-trip.
            var workflowState = await RuntimeExecutionExpressionCarrier.LoadWorkflowStateAsync(serviceProvider, workItem.WorkflowExecutionId, cancellationToken);

            parentActivity.NodeId = parentExecutableNode.ExecutableNodeId;
            parentActivity.Id = payload.ActivityExecutionId;

            // Build the container's self-owner scope (ADR 0030 parent-completion path): the enclosing container
            // chain, anchored by the workflow-scope variables from the current durable-value projection (#286), so
            // an OnChildCompleted/OnChildFaulted expression resolves enclosing-container/workflow variables by name
            // rather than falling back to the flat carrier projection. `evaluateAsSelf` suppresses the per-iteration
            // loop layer: the container's own provenance carries the iteration values of the loop it is a body of
            // (ADR 0028), which is the wrong innermost scope when the container evaluates itself and broke loop/break
            // control flow during development.
            var variableScope = await scopeService.BuildScopeAsync(
                executable, workItem.WorkflowExecutionId, parentState, cancellationToken, constructedParent.WorkflowVariables, evaluateAsSelf: true);

            // Populate the live execution-time expression carrier (ADR 0030) for the container's child-completion
            // logic: workflow identity and the durable-value projections for inputs/variables/outputs. Previously
            // this context carried identity but none of the carrier state, so an OnChildCompleted/OnChildFaulted
            // handler evaluating an expression saw empty getCorrelationId()/getInput()/getVariable()/getOutput().
            // The carrier's WorkflowVariables projection serves flat getVariable reads; the threaded self-owner
            // scope above serves named/structured resolution of enclosing-container and workflow variables.
            var carrier = RuntimeExecutionExpressionCarrier.Create(
                workflowState, payload.PinnedExecutable, constructedParent.WorkflowInputs, constructedParent.WorkflowVariables, constructedParent.ActivityOutputValues);
            context = SimpleActivityExecutionContext.ForExecution(
                serviceProvider,
                parentActivity,
                cancellationToken,
                workItem.WorkflowExecutionId,
                payload.PinnedExecutable,
                workItem,
                parentExecutableNode,
                parentState,
                variableScope,
                carrier);
            RuntimeActivityInputMemory.Seed(context, constructedParent.Inputs);

            if (childFaulted)
            {
                var childFaultedContext = new ActivityChildFaultedContext(
                    context,
                    completedChildActivityExecutionId,
                    completedChildState.Execution.ExecutableNodeId,
                    ReadIncidentId(workItem),
                    completedChildState.IterationId);

                await ((IActivityChildFaultHandler)parentActivity).OnChildFaultedAsync(childFaultedContext);
            }
            else
            {
                var childCompletedContext = new ActivityChildCompletedContext(
                    context,
                    completedChildActivityExecutionId,
                    completedChildState.Execution.ExecutableNodeId,
                    payload.OutcomeNames,
                    completedChildState.IterationId);

                await ((IActivityChildCompletionHandler)parentActivity).OnChildCompletedAsync(childCompletedContext);
            }

            // Persist any enclosing-container variable the child-completion/fault logic assigned by name back to
            // the owning container executions' snapshots, so sibling branches and resume observe it (ADR 0027) —
            // mirroring the leaf-invoke path. This covers the container scopes in the visible chain; write-back of
            // the workflow-root scope to durable-value state (which must fold into the checkpoint commit) remains a
            // documented follow-up, so a mutation of a workflow-level variable here is not yet durable.
            await scopeService.PersistScopeMutationsAsync(variableScope, workItem.WorkflowExecutionId, cancellationToken);

            var scheduledChildren = context.GetChildActivityScheduleRequests();
            if (context.CompositeCompletionRequested && scheduledChildren.Count > 0)
                throw new InvalidOperationException("Composite activity cannot both request completion and schedule child activities in the same child-completion evaluation.");
        }
        catch (OperationCanceledException)
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

            var latestFaultedParentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken)
                                           ?? parentState;
            await activityFaultIncidentRecorder.CommitAsync(NewFaultIncidentRecordRequest(checkpointCommitter, workItem, payload, latestFaultedParentState, exception, "ParentCompletionFaulted", valueSnapshots), cancellationToken);
            return;
        }

        var childScheduleRequests = context.GetChildActivityScheduleRequests();
        if (childScheduleRequests.Count > 0)
        {
            if (checkpointCommitter is null || inspectionAccumulator is null)
            {
                await EnqueueChildActivityScheduleWorkAsync(schedulerWorkQueue, idGenerator, workItem, payload, childScheduleRequests, cancellationToken);
                return;
            }

            await CommitChildSchedulingParentActivityAsync(checkpointCommitter, inspectionAccumulator, idGenerator, workItem, payload, parentState, childScheduleRequests, valueSnapshots, cancellationToken);
            return;
        }

        if (!context.CompositeCompletionRequested && context.CompositeCompletionDeferred)
            return;

        if (!context.CompositeCompletionRequested)
            throw new InvalidOperationException($"Composite activity execution '{payload.ActivityExecutionId}' did not request completion, child activity scheduling, or deferred completion after child execution '{completedChildActivityExecutionId}' completed.");

        var latestParentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken)
                                ?? parentState;

        // A completing container's scope is no longer live for runtime expressions; mark it completed
        // and retain its final variable values as inspection evidence only through the configured
        // capture/retention policy (ADR 0027, #210).
        var containerVariableSnapshots = RuntimeContainerVariableEvidence.Capture(
            payloadCapturePolicy, scopeService, parentExecutableNode, latestParentState,
            workItem.WorkflowExecutionId, payload.ActivityExecutionId, workItem.WorkItemId, _timeProvider.GetUtcNow());
        var completedParentState = CompleteParentActivity(workItem, payload, latestParentState, context.CompositeCompletionOutcomeNames);

        // Only a container that actually owns scoped variables gets its scope marked completed; this
        // keeps the completion of ordinary containers untouched (ADR 0027, #210).
        if (containerVariableSnapshots.Count > 0)
            completedParentState = RuntimeContainerScopeService.MarkScopeCompleted(completedParentState);

        if (checkpointCommitter is null || inspectionAccumulator is null)
        {
            await activityExecutionStateStore.SaveAsync(completedParentState, cancellationToken);
            await EnqueueCompletionWorkAsync(schedulerWorkQueue, workItem, payload, completedParentState, cancellationToken);
            return;
        }

        await CommitCompletedParentActivityAsync(checkpointCommitter, inspectionAccumulator, workItem, payload, completedParentState, ReadCompletionOutcomeNames(completedParentState), containerVariableSnapshots, cancellationToken);
    }

    private async ValueTask<ConstructedActivity> ConstructActivityAsync(
        IServiceProvider serviceProvider,
        IActivityFactory activityFactory,
        IRuntimeActivityOutputRegister activityOutputRegister,
        IDurableValueStateStore durableValueStateStore,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        ExecutableNode executableNode,
        CancellationToken cancellationToken)
    {
        var durableValues = await durableValueStateStore.ListAsync(workItem.WorkflowExecutionId, cancellationToken);
        var projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
        var workflowVariables = projections.WorkflowVariables;
        var workflowInputs = projections.WorkflowInputs;
        var activityOutputValues = projections.ActivityOutputValues;

        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: workItem.WorkflowExecutionId,
            activityExecutionId: payload.ActivityExecutionId,
            durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
            activityOutputs: activityOutputRegister,
            serviceProvider: serviceProvider,
            workflowVariables: workflowVariables,
            workflowInputs: workflowInputs,
            activityOutputValues: activityOutputValues);
        var inputs = await _inputMaterializer.MaterializeInputsAsync(executableNode, resolutionContext, cancellationToken);

        var activity = await activityFactory.Create(
            executableNode.DescriptorType,
            executableNode.DescriptorPayload,
            inputs.ToDictionary(input => input.Name, input => input.Argument, StringComparer.OrdinalIgnoreCase),
            BuildOutputArguments(executableNode),
            cancellationToken);

        return new ConstructedActivity(activity, inputs, workflowInputs, workflowVariables, activityOutputValues);
    }

    private async ValueTask EnqueueChildActivityScheduleWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        CancellationToken cancellationToken)
    {
        foreach (var workItem in NewChildActivityScheduleWorkItems(idGenerator, parentCompletionWorkItem, parentCompletionPayload, scheduleRequests))
            await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
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
                    : request.SchedulingProvenance);

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

    private async ValueTask CommitChildSchedulingParentActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        ActivityExecutionState parentState,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
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
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            parentState,
            checkpointId,
            occurredAt,
            valueSnapshots: valueSnapshots,
            metadata: metadata,
            cancellationToken: cancellationToken);
        var childWorkItems = NewChildActivityScheduleWorkItems(idGenerator, parentCompletionWorkItem, parentCompletionPayload, scheduleRequests).ToArray();
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{parentCompletionWorkItem.WorkItemId}:activity-inspection-captured:{parentCompletionPayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: parentCompletionWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [parentCompletionPayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: parentCompletionPayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: childWorkItems
                .Select(workItem => NewEnqueueSchedulerWorkIntent(parentCompletionWorkItem, parentCompletionPayload.ActivityExecutionId, workItem, occurredAt))
                .ToArray(),
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private async ValueTask EnqueueCompletionWorkAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem parentCompletionWorkItem,
        RuntimeCompleteActivityCommandPayload parentCompletionPayload,
        ActivityExecutionState completedParentState,
        CancellationToken cancellationToken)
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

        var workItem = new RuntimeSchedulerWorkItem(
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

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private async ValueTask CommitCompletedParentActivityAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
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
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
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
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: parentCompletionPayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: [NewEnqueueSchedulerWorkIntent(parentCompletionWorkItem, parentCompletionPayload.ActivityExecutionId, completionWorkItem, occurredAt)],
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

    private async ValueTask EnqueueContinuationSchedulingAsync(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeSchedulerWorkItem sourceWorkItem,
        RuntimeCompleteActivityCommandPayload sourcePayload,
        CancellationToken cancellationToken)
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

        var workItem = new RuntimeSchedulerWorkItem(
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

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private ActivityExecutionState CompleteParentActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        ActivityExecutionState state,
        IReadOnlyCollection<string> outcomeNames)
    {
        var normalizedOutcomeNames = NormalizeOutcomeNames(outcomeNames, defaultToDone: true);
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.InvokeReason] = payload.Reason;
        metadata[RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(normalizedOutcomeNames);

        return state with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = _timeProvider.GetUtcNow(),
            Metadata = metadata
        };
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

    private static RuntimeCompleteActivityCommandPayload DeserializeCompletePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is null)
            throw new InvalidOperationException("CompleteActivity scheduler work item requires a complete activity payload.");

        try
        {
            // RT-11: reuse the single per-work-item parse rather than deserializing the payload again.
            return RuntimeCompleteActivityPayloadMemo.Deserialize(workItem)
                   ?? throw new InvalidOperationException("CompleteActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsCompletePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("CompleteActivity scheduler work item payload is not a valid complete activity payload.", exception);
        }
    }

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

    private static IDictionary<string, OutputArgument> BuildOutputArguments(ExecutableNode executableNode) =>
        executableNode.OutputCaptures.ToDictionary(
            item => item.Key,
            item => (OutputArgument)new OutputArgument<object?>(new RuntimeOutputMemoryBlockReference(item.Key)),
            StringComparer.Ordinal);

    private static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildInputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        IReadOnlyCollection<RuntimeMaterializedActivityInput> inputs,
        DateTimeOffset capturedAt) =>
        inputs
            .Select(input =>
            {
                var type = TypeDescriptorFor(input.Value);
                var decision = payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
                    RuntimePayloadCaptureSubject.ActivityInput,
                    workItem.WorkflowExecutionId,
                    capturedAt,
                    activityExecutionId: payload.ActivityExecutionId,
                    valueName: input.Name,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = payload.ExecutableNodeId,
                        [RuntimeMetadataKeys.ParentCompletionSchedulerWorkItemId] = workItem.WorkItemId
                    }));
                return ActivityExecutionInspectionValueSnapshot.FromDecision(
                    input.Name,
                    ActivityExecutionInspectionValueSubject.ActivityInput,
                    decision,
                    type,
                    capturedAt,
                    SerializeCapturedValue(decision, input.Value),
                    isSensitive: false,
                    metadata: decision.Metadata);
            })
            .ToArray();

    private static JsonElement SerializeValue(object? value) =>
        value is JsonElement json
            ? json.Clone()
            : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));

    private static JsonElement? SerializeCapturedValue(RuntimePayloadCaptureDecision decision, object? value) =>
        decision.CapturesPayload ? SerializeValue(value) : null;

    private static RuntimeValueTypeDescriptor RuntimeObjectType { get; } = new("clr", typeof(object).FullName, null);

    private static RuntimeValueTypeDescriptor TypeDescriptorFor(object? value) =>
        value is null ? RuntimeObjectType : new RuntimeValueTypeDescriptor("clr", value.GetType().FullName, null);

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

            return NormalizeOutcomeNames(outcomeNames, defaultToDone: false);
        }

        return [ActivityOutcomes.Done];
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

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"CompleteActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private sealed class RuntimeOutputMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;

        public IMemoryBlock Declare() => new RuntimeOutputMemoryBlock();

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
            context.Get<T>(this);

        public T? Get<T>(IExpressionExecutionContext context) =>
            context.Get<T>(this);
    }

    private sealed class RuntimeOutputMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }

    private sealed record ConstructedActivity(
        IActivity Activity,
        IReadOnlyList<RuntimeMaterializedActivityInput> Inputs,
        IReadOnlyDictionary<string, object?> WorkflowInputs,
        IReadOnlyDictionary<string, object?> WorkflowVariables,
        IReadOnlyDictionary<string, object?> ActivityOutputValues);
}

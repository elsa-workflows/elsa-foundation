using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowStartActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowStartActivitySchedulerWorkHandler);

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly RuntimeCheckpointCommitter? _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly TimeProvider _timeProvider;
    private readonly IRuntimeActivityInputMaterializer? _inputMaterializer;
    private readonly IDurableValueStateStore? _durableValueStateStore;
    private readonly IRuntimeActivityOutputRegister? _activityOutputRegister;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    public WorkflowStartActivitySchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeCheckpointCommitter? checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider,
        IRuntimeActivityInputMaterializer? inputMaterializer = null,
        IDurableValueStateStore? durableValueStateStore = null,
        IRuntimeActivityOutputRegister? activityOutputRegister = null,
        IServiceScopeFactory? serviceScopeFactory = null)
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
        _inputMaterializer = inputMaterializer;
        _durableValueStateStore = durableValueStateStore;
        _activityOutputRegister = activityOutputRegister;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.StartActivity;
    }

    /// <summary>Direct (no-pipeline) dispatch: run the handler and commit its checkpoint inline (when one is produced).</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        RuntimeCheckpointCommit? commit;
        if (_serviceScopeFactory is null)
        {
            commit = await ExecuteAsync(workItem, serviceProvider: null, cancellationToken);
        }
        else
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            commit = await ExecuteAsync(workItem, scope.ServiceProvider, cancellationToken);
        }

        if (commit is not null)
            await _checkpointCommitter!.CommitAsync(commit, cancellationToken);
    }

    /// <summary>Pipeline dispatch (Move 2): run the handler in the Invoke slot and stage its commit for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        var commit = await ExecuteAsync(workItem, pipelineContext.Workspace.AmbientServices, cancellationToken);
        if (commit is not null)
            pipelineContext.Workspace.StageCheckpointCommit(commit);
    }

    private async ValueTask<RuntimeCheckpointCommit?> ExecuteAsync(
        RuntimeSchedulerWorkItem workItem,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var startPayload = DeserializeStartPayload(workItem);
        var executable = await _workflowExecutableStore.FindAsync(startPayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(startPayload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, startPayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(startPayload.ExecutableNodeId, out var executableNode))
            throw new InvalidOperationException($"StartActivity scheduler work item '{workItem.WorkItemId}' references executable node '{startPayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var state = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, startPayload.ActivityExecutionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException($"StartActivity scheduler work item '{workItem.WorkItemId}' references missing activity execution '{startPayload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, startPayload.ExecutableNodeId))
            throw new InvalidOperationException($"StartActivity scheduler work item '{workItem.WorkItemId}' references executable node '{startPayload.ExecutableNodeId}', but activity execution '{startPayload.ActivityExecutionId}' belongs to executable node '{state.Execution.ExecutableNodeId}'.");

        if (state.Status == ActivityExecutionStatus.Running)
        {
            if (executableNode.ActivityContract is not null && state.InputSnapshot is null)
                throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{state.InvocationId}' has no committed input snapshot.");

            await EnqueueInvokeActivityAsync(workItem, startPayload, cancellationToken);
            return null;
        }

        if (state.Status != ActivityExecutionStatus.Scheduled)
            return null;

        var startedAt = _timeProvider.GetUtcNow();
        ActivityInputSnapshot? inputSnapshot = null;
        ActivityAttempt? firstAttempt = null;
        if (executableNode.ActivityContract is not null)
        {
            inputSnapshot = await MaterializeInputSnapshotAsync(
                executable,
                executableNode,
                state,
                serviceProvider,
                startedAt,
                cancellationToken);
            firstAttempt = new ActivityAttempt(
                $"{state.InvocationId}:attempt:1",
                state.InvocationId,
                1,
                ActivityAttemptReason.Initial,
                startedAt);
        }

        var runningState = StartActivity(workItem, startPayload, state, executableNode.ActivityContract, inputSnapshot, firstAttempt, startedAt);
        if (_checkpointCommitter is null || _inspectionAccumulator is null)
        {
            await _activityExecutionStateStore.SaveAsync(runningState, cancellationToken);
            await EnqueueInvokeActivityAsync(workItem, startPayload, cancellationToken);
            return null;
        }

        return await NewCommitAsync(workItem, startPayload, runningState, cancellationToken);
    }

    private static RuntimeStartActivityCommandPayload DeserializeStartPayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "StartActivity scheduler work item requires a start activity payload.",
            resolvedToNullMessage: "StartActivity scheduler work item payload resolved to null.",
            invalidPayloadMessage: "StartActivity scheduler work item payload is not a valid start activity payload.",
            deserialize: static (_, payload) => payload.Deserialize<RuntimeStartActivityCommandPayload>(),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException ||
                exception is ArgumentException argumentException && IsStartPayloadValidationException(argumentException));

    private static bool IsStartPayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason";

    private ActivityExecutionState StartActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeStartActivityCommandPayload startPayload,
        ActivityExecutionState state,
        ActivityContract? contract,
        ActivityInputSnapshot? inputSnapshot,
        ActivityAttempt? firstAttempt,
        DateTimeOffset startedAt)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.StartReason] = startPayload.Reason;
        metadata[RuntimeMetadataKeys.StartSchedulerWorkItemId] = workItem.WorkItemId;

        return state with
        {
            Status = ActivityExecutionStatus.Running,
            StartedAt = startedAt,
            ContractIdentity = contract is null
                ? state.ContractIdentity
                : new ActivityInvocationContractIdentity(contract.ActivityTypeKey, contract.ContractVersion, contract.SchemaFingerprint),
            InputSnapshot = inputSnapshot ?? state.InputSnapshot,
            Attempts = firstAttempt is null ? state.Attempts : [firstAttempt],
            Metadata = metadata
        };
    }

    private async ValueTask<ActivityInputSnapshot> MaterializeInputSnapshotAsync(
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        IServiceProvider? serviceProvider,
        DateTimeOffset materializedAt,
        CancellationToken cancellationToken)
    {
        var inputMaterializer = _inputMaterializer
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires an input snapshot materializer.");
        var durableValueStateStore = _durableValueStateStore
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires a durable value state store.");
        var activityOutputRegister = _activityOutputRegister
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires an activity output register.");

        var durableValues = await durableValueStateStore.ListAsync(state.Execution.WorkflowExecutionId, cancellationToken);
        var runtimeView = await _activityExecutionStateStore.ListAsync(state.Execution.WorkflowExecutionId, cancellationToken);
        var projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
        var variableScope = await new RuntimeContainerScopeService(_activityExecutionStateStore).BuildScopeAsync(
            executable,
            state.Execution.WorkflowExecutionId,
            state,
            cancellationToken,
            projections.WorkflowVariables);
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: state.Execution.WorkflowExecutionId,
            activityExecutionId: state.InvocationId,
            durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
            activityOutputs: activityOutputRegister,
            serviceProvider: serviceProvider,
            workflowVariables: projections.WorkflowVariables,
            workflowInputs: projections.WorkflowInputs,
            activityOutputValues: projections.ActivityOutputValues,
            variableScope: variableScope,
            consumerInvocation: state,
            runtimeView: runtimeView,
            executable: executable,
            workflowInputEnvelopes: projections.WorkflowInputEnvelopes,
            variableEnvelopes: projections.VariableEnvelopes);

        return await inputMaterializer.MaterializeSnapshotAsync(
            executableNode,
            state.InvocationId,
            resolutionContext,
            materializedAt,
            cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit> NewCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeStartActivityCommandPayload startPayload,
        ActivityExecutionState runningState,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:activity-started:{startPayload.ActivityExecutionId}";
        var metadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = startPayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = startPayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = startPayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = startPayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = startPayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = startPayload.PinnedExecutable.ArtifactHash
        });
        var inspection = await _inspectionAccumulator!.BuildProjectionAsync(runningState, checkpointId, occurredAt, metadata: metadata, cancellationToken: cancellationToken);
        var invokeWorkItem = NewInvokeActivityWorkItem(workItem, startPayload);

        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:activity-started:{startPayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityStarted,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [startPayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: startPayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: runningState,
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: startPayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: [SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(workItem, startPayload.ActivityExecutionId, invokeWorkItem, occurredAt)],
            Metadata: metadata);
    }

    private async ValueTask EnqueueInvokeActivityAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        CancellationToken cancellationToken)
    {
        var workItem = NewInvokeActivityWorkItem(startWorkItem, startPayload);
        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private RuntimeSchedulerWorkItem NewInvokeActivityWorkItem(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeInvokeActivityCommandPayload(
            startPayload.PinnedExecutable,
            startPayload.ExecutableNodeId,
            startPayload.ActivityExecutionId,
            RuntimeInvokeActivityCommandPayload.StartedActivityReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{startWorkItem.WorkItemId}:invoke:{startPayload.ActivityExecutionId}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: $"{startWorkItem.CommandId}:invoke:{startPayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: $"{startWorkItem.IdempotencyKey}:invoke:{startPayload.ActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: startWorkItem.CommandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata);
    }

}

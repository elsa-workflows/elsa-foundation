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
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator _inspectionAccumulator;
    private readonly TimeProvider _timeProvider;
    private readonly IDurableValueStateStore? _durableValueStateStore;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly IWorkflowExecutableReader? _executableReader;

    public WorkflowStartActivitySchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        TimeProvider timeProvider,
        IServiceScopeFactory serviceScopeFactory,
        IDurableValueStateStore? durableValueStateStore = null,
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IWorkflowExecutableReader? executableReader = null)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(inspectionAccumulator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _workflowExecutableStore = workflowExecutableStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _timeProvider = timeProvider;
        _durableValueStateStore = durableValueStateStore;
        _serviceScopeFactory = serviceScopeFactory;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _executableReader = executableReader;
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
        var commit = await ExecuteWithServicesAsync(workItem, ambientServices: null, cancellationToken);

        if (commit is not null)
            await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>Pipeline dispatch (Move 2): run the handler in the Invoke slot and stage its commit for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        var commit = await ExecuteWithServicesAsync(workItem, pipelineContext.Workspace.AmbientServices, cancellationToken);
        if (commit is not null)
            pipelineContext.Workspace.StageCheckpointCommit(commit);
    }

    private async ValueTask<RuntimeCheckpointCommit?> ExecuteWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        IServiceProvider? ambientServices,
        CancellationToken cancellationToken)
    {
        if (ambientServices is not null)
            return await ExecuteAsync(workItem, ambientServices, cancellationToken);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        return await ExecuteAsync(workItem, scope.ServiceProvider, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit?> ExecuteAsync(
        RuntimeSchedulerWorkItem workItem,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var resolution = await ResolveStartAsync(workItem, serviceProvider, cancellationToken);
        var startPayload = resolution.StartPayload;
        var executable = resolution.Executable;
        var executableNode = resolution.ExecutableNode;
        var state = resolution.State;

        if (executableNode.IntrinsicKind is not null)
        {
            if (state.Status == ActivityExecutionStatus.Completed)
                return null;
            if (state.Status != ActivityExecutionStatus.Scheduled)
                throw new InvalidOperationException($"Intrinsic execution '{state.InvocationId}' must be Scheduled before execution; current status is '{state.Status}'.");

            var executor = serviceProvider.GetService<WorkflowIntrinsicExecutor>()
                ?? throw new InvalidOperationException($"Executable node '{executableNode.ExecutableNodeId}' requires the workflow intrinsic executor.");
            return await executor.ExecuteAsync(
                workItem,
                startPayload,
                executable,
                executableNode,
                state,
                cancellationToken);
        }

        if (executableNode.ActivityContract is null)
            throw new InvalidOperationException($"VF-ACT-001: Executable CLR activity node '{executableNode.ExecutableNodeId}' has no pinned activity contract.");

        if (state.Status == ActivityExecutionStatus.Running)
        {
            if (state.InputSnapshot is null)
                throw new InvalidOperationException($"VF-ACT-009: Running typed activity invocation '{state.InvocationId}' has no committed input snapshot.");

            await EnqueueInvokeActivityAsync(workItem, startPayload, cancellationToken);
            return null;
        }

        if (state.Status != ActivityExecutionStatus.Scheduled)
            return null;

        var runningState = await ProduceRunningStateAsync(workItem, startPayload, executable, executableNode, state, serviceProvider, cancellationToken);
        return await NewCommitAsync(workItem, startPayload, runningState, cancellationToken);
    }

    /// <summary>
    /// Fused-mode start stage (spec 123 D1): runs the same resolve → materialize → transition-to-Running →
    /// <c>ActivityStarted</c> commit path the discrete handler runs, but for a fresh Scheduled typed ReplaySafe leaf
    /// commits the intent-free <c>ActivityStarted</c> checkpoint (buffered into the active coalescing session) and
    /// returns the derived <c>InvokeActivity</c> work item for the driver to dispatch inline — never enqueuing it.
    /// Returns <see langword="null"/> for anything that is not a fresh Scheduled ReplaySafe leaf (intrinsic, already
    /// Running/Completed, non-ReplaySafe): the driver then falls back to the discrete queue for that work item.
    /// </summary>
    internal async ValueTask<RuntimeSchedulerWorkItem?> ExecuteFusedStartAsync(
        RuntimeSchedulerWorkItem workItem,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var resolution = await ResolveStartAsync(workItem, serviceProvider, cancellationToken);
        var executableNode = resolution.ExecutableNode;
        var state = resolution.State;

        // Fusion only continues for a fresh Scheduled typed ReplaySafe leaf; every other shape falls back so the
        // discrete chain (intrinsic executor, Running re-enqueue, no-op) runs unchanged.
        if (executableNode.IntrinsicKind is not null ||
            executableNode.ActivityContract?.SideEffectProfile != SideEffectProfile.ReplaySafe ||
            state.Status != ActivityExecutionStatus.Scheduled)
            return null;

        var runningState = await ProduceRunningStateAsync(workItem, resolution.StartPayload, resolution.Executable, executableNode, state, serviceProvider, cancellationToken);
        var core = await BuildStartedCommitAsync(workItem, resolution.StartPayload, runningState, cancellationToken);
        await _checkpointCommitter.CommitAsync(core.Commit, cancellationToken);
        return core.InvokeWorkItem;
    }

    // Resolve → validate → optional variable-frame activation. Shared verbatim by the discrete handler and the fused
    // start stage so the two paths never diverge on how they read and prepare the activity execution.
    private async ValueTask<StartResolution> ResolveStartAsync(
        RuntimeSchedulerWorkItem workItem,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var startPayload = DeserializeStartPayload(workItem);
        var executable = await PinnedExecutableRead.FindAsync(_executableReader, _workflowExecutableStore, startPayload.PinnedExecutable.ArtifactId, cancellationToken);
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

        state.EnsureValueFlowCompatible();

        var requiresFrameActivation = state.IterationFrameRequest is not null ||
                                      (!StringComparer.Ordinal.Equals(executableNode.ExecutableNodeId, executable.RootActivity.ExecutableNodeId) &&
                                       new RuntimeVariableDeclarationProjector().ProjectDeclarations(executableNode).Count > 0);
        if (state.Status == ActivityExecutionStatus.Scheduled && requiresFrameActivation)
        {
            var workflowExecutionStateStore = _workflowExecutionStateStore
                ?? serviceProvider.GetService<IWorkflowExecutionStateStore>()
                ?? throw new InvalidOperationException($"Activity '{state.InvocationId}' requires the workflow variable-frame owner store.");
            state = await new RuntimeContainerScopeService(_activityExecutionStateStore, workflowExecutionStateStore)
                .ActivateOwnedFramesAsync(executable, executableNode, state, state.IterationFrameRequest, cancellationToken);
        }

        return new StartResolution(startPayload, executable, executableNode, state);
    }

    // Materialize the input snapshot and transition Scheduled → Running. Shared by the discrete handler and the fused
    // start stage; the only difference downstream is whether the resulting ActivityStarted commit carries the
    // InvokeActivity continuation intent (discrete) or is dispatched inline (fused).
    private async ValueTask<ActivityExecutionState> ProduceRunningStateAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode executableNode,
        ActivityExecutionState state,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        var inputSnapshot = await MaterializeInputSnapshotAsync(
            executable,
            executableNode,
            state,
            serviceProvider,
            startedAt,
            cancellationToken);
        var firstAttempt = new ActivityAttempt(
            $"{state.InvocationId}:attempt:1",
            state.InvocationId,
            1,
            ActivityAttemptReason.Initial,
            startedAt);

        return StartActivity(workItem, startPayload, state, executableNode.ActivityContract, inputSnapshot, firstAttempt, startedAt);
    }

    private readonly record struct StartResolution(
        RuntimeStartActivityCommandPayload StartPayload,
        WorkflowExecutable Executable,
        ExecutableNode ExecutableNode,
        ActivityExecutionState State);

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
        IServiceProvider serviceProvider,
        DateTimeOffset materializedAt,
        CancellationToken cancellationToken)
    {
        var inputMaterializer = serviceProvider.GetService<IRuntimeActivityInputMaterializer>()
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires an input snapshot materializer.");
        var durableValueStateStore = _durableValueStateStore
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires a durable value state store.");
        var durableValues = await durableValueStateStore.ListAllDurableValueStatesAsync(state.Execution.WorkflowExecutionId, cancellationToken);
        var runtimeView = await _activityExecutionStateStore.ListAllAsync(state.Execution.WorkflowExecutionId, cancellationToken);
        var projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
        var workflowExecutionStateStore = _workflowExecutionStateStore
            ?? serviceProvider.GetService<IWorkflowExecutionStateStore>()
            ?? throw new InvalidOperationException($"Typed activity invocation '{state.InvocationId}' requires the canonical workflow variable-frame owner store.");
        var variableEnvelopes = (await new RuntimeContainerScopeService(_activityExecutionStateStore, workflowExecutionStateStore)
                .BuildVisibleFramesAsync(state.Execution.WorkflowExecutionId, state, cancellationToken: cancellationToken))
            .Values;
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: state.Execution.WorkflowExecutionId,
            activityExecutionId: state.InvocationId,
            consumerInvocation: state,
            runtimeView: runtimeView,
            executable: executable,
            workflowInputEnvelopes: projections.WorkflowInputEnvelopes,
            variableEnvelopes: variableEnvelopes);

        return await inputMaterializer.MaterializeSnapshotAsync(
            executableNode,
            state.InvocationId,
            resolutionContext,
            materializedAt,
            cancellationToken);
    }

    /// <summary>
    /// Discrete adapter: builds the fused-mode <c>ActivityStarted</c> commit core and re-attaches the
    /// <c>InvokeActivity</c> continuation intent, reproducing today's commit byte-for-byte. The commit builder and the
    /// derived <c>InvokeActivity</c> work item are extracted into <see cref="BuildStartedCommitAsync"/> so the spec-123
    /// fusion driver can commit the same stage without the continuation intent and dispatch the invoke handler inline.
    /// </summary>
    private async ValueTask<RuntimeCheckpointCommit> NewCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeStartActivityCommandPayload startPayload,
        ActivityExecutionState runningState,
        CancellationToken cancellationToken)
    {
        var core = await BuildStartedCommitAsync(workItem, startPayload, runningState, cancellationToken);
        return core.Commit with
        {
            PostCommitIntents =
            [
                SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                    workItem, startPayload.ActivityExecutionId, core.InvokeWorkItem, core.OccurredAt)
            ]
        };
    }

    /// <summary>
    /// The <c>ActivityStarted</c> stage core (spec 123 FR-002): produces the <c>ActivityStarted</c> checkpoint commit
    /// <b>without</b> its <c>InvokeActivity</c> post-commit intent, alongside the derived <c>InvokeActivity</c> work
    /// item and the checkpoint's occurrence time. The discrete handler re-attaches the intent
    /// (<see cref="NewCommitAsync"/>); the fused driver commits the intent-free commit and dispatches the returned work
    /// item through the unchanged invoke handler inline.
    /// </summary>
    internal async ValueTask<StartedCommitCore> BuildStartedCommitAsync(
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
        var inspection = await _inspectionAccumulator.BuildProjectionAsync(runningState, checkpointId, occurredAt, metadata: metadata, cancellationToken: cancellationToken);
        var invokeWorkItem = NewInvokeActivityWorkItem(workItem, startPayload);

        var commit = new RuntimeCheckpointCommit(
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
            PostCommitIntents: [],
            Metadata: metadata);

        return new StartedCommitCore(commit, invokeWorkItem, occurredAt);
    }

    /// <summary>
    /// The intent-free <c>ActivityStarted</c> commit plus the derived <c>InvokeActivity</c> continuation work item and
    /// the checkpoint's occurrence time (spec 123 FR-002).
    /// </summary>
    internal readonly record struct StartedCommitCore(
        RuntimeCheckpointCommit Commit,
        RuntimeSchedulerWorkItem InvokeWorkItem,
        DateTimeOffset OccurredAt);

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
            workItemId: RuntimeChainId.Derive(startWorkItem.WorkItemId, $"invoke:{startPayload.ActivityExecutionId}"),
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(startWorkItem.CommandId, $"invoke:{startPayload.ActivityExecutionId}"),
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(startWorkItem.IdempotencyKey, $"invoke:{startPayload.ActivityExecutionId}"),
            enqueuedAt: now,
            recordedAt: now,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: startWorkItem.CommandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata,
            executionScopeId: startWorkItem.ExecutionScopeId,
            attempt: startWorkItem.Attempt);
    }

}

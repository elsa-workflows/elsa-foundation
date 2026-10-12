using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Workflows.Runtime.Services.WorkHandlers;

public sealed class WorkflowCheckpointSchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowCheckpointSchedulerWorkHandler);

    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly IWorkflowExecutableStore? _workflowExecutableStore;
    private readonly IRuntimeCoalescingSessionAccessor? _sessionAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowExecutableReader? _executableReader;

    /// <summary>
    /// Constructs the handler. <paramref name="workflowExecutionStateStore"/> is optional: when supplied (the
    /// DI default — it is a registered service), the handler preserves durable instance fields (correlation id,
    /// parent, tenant) across the workflow-started/completed transitions; when null it falls back to the prior
    /// behaviour of rebuilding workflow state from the checkpoint payload alone. <paramref name="sessionAccessor"/>
    /// gives the persistence mode the drain runs in, which the workflow-started checkpoint records on the run.
    /// </summary>
    public WorkflowCheckpointSchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider,
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IWorkflowExecutableStore? workflowExecutableStore = null,
        IRuntimeCoalescingSessionAccessor? sessionAccessor = null,
        IWorkflowExecutableReader? executableReader = null)
    {
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _activityExecutionStateStore = activityExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _workflowExecutableStore = workflowExecutableStore;
        _sessionAccessor = sessionAccessor;
        _timeProvider = timeProvider;
        _executableReader = executableReader;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.Checkpoint;
    }

    /// <summary>Direct (no-pipeline) dispatch: build the checkpoint commit and commit it inline.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializeCheckpointPayload(workItem);
        var commit = await BuildCommitAsync(workItem, payload, cancellationToken);
        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>Pipeline dispatch: build the commit in the Invoke slot and stage it for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(pipelineContext);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializeCheckpointPayload(workItem);
        pipelineContext.Workspace.PendingCheckpointCommit = await BuildCommitAsync(workItem, payload, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit> BuildCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}";
        var commitId = $"commit:{workItem.WorkItemId}";
        var activityStateChanges = new List<RuntimeStateChange<ActivityExecutionState>>();
        var activityInspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>();
        var activityStateChangeMetadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason
        });

        foreach (var activityExecutionId in payload.ActivityExecutionIds)
        {
            var state = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, activityExecutionId, cancellationToken);
            if (state is null)
                throw new InvalidOperationException($"Checkpoint scheduler work item '{workItem.WorkItemId}' references missing activity execution '{activityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");
            ValidateTerminalCheckpointStatus(workItem, payload, state);

            activityStateChanges.Add(new RuntimeStateChange<ActivityExecutionState>(
                StateId: activityExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: state,
                Metadata: activityStateChangeMetadata));
            if (_inspectionAccumulator is not null)
            {
                var inspection = await _inspectionAccumulator.BuildProjectionAsync(
                    state,
                    checkpointId,
                    occurredAt,
                    metadata: activityStateChangeMetadata,
                    cancellationToken: cancellationToken);
                activityInspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                    StateId: activityExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: inspection,
                    Metadata: activityStateChangeMetadata));
            }
        }

        var checkpointMetadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
            [RuntimeMetadataKeys.ExecutableArtifactId] = payload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = payload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = payload.PinnedExecutable.ArtifactHash
        });

        // Preserve durable instance fields (correlation id, parent, tenant) that earlier checkpoints set —
        // e.g. a Correlate leaf — across the workflow-completed transition, which otherwise rebuilds the
        // workflow state from the checkpoint payload alone.
        var priorWorkflowState = _workflowExecutionStateStore is null
            ? null
            : await _workflowExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);

        var executable = StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowStarted) && _workflowExecutableStore is not null
            ? await PinnedExecutableRead.FindAsync(_executableReader, _workflowExecutableStore, payload.PinnedExecutable.ArtifactId, cancellationToken)
            : null;

        // At start, record the mode this drain runs the run in on the durable instance. Later drains read it back so the
        // run keeps its mode, and the read model reports the mode the run actually used. Other checkpoints carry it
        // forward via PreserveSystemMetadata.
        var persistenceMode = StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowStarted)
            ? DrainModeOf(workItem.WorkflowExecutionId)
            : null;

        return new RuntimeCheckpointCommit(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: payload.CheckpointName,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: payload.ActivityExecutionIds,
                Metadata: checkpointMetadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: BuildWorkflowExecutionStateChange(workItem, payload, occurredAt, priorWorkflowState, executable, persistenceMode),
                scheduler: null,
                activityExecutions: activityStateChanges.ToArray(),
                bookmarks: [],
                durableValues: BuildSeedDurableValueChanges(workItem, payload, occurredAt),
                incidents: [],
                operational: [],
                activityExecutionInspections: activityInspectionChanges.ToArray()),
            PostCommitIntents: payload.PostCommitIntents.ToArray(),
            Metadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CommandKind] = workItem.CommandKind.ToString()
            }));
    }

    private static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildSeedDurableValueChanges(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt)
    {
        // SeedVariables are NOT written to the durable-value channel (#972): caller-supplied variable
        // values overlay the root variable frame in CreateRootVariableFrame, the single runtime truth.
        if (payload.SeedInputs.Count == 0 && payload.SeedStimulusInput is null && string.IsNullOrWhiteSpace(payload.SeedTriggerNodeId) && payload.SeedTriggerMetadata.Count == 0)
            return [];

        return RuntimeWorkflowStateSeed.BuildSeedChanges(
            workItem.WorkflowExecutionId,
            payload.SeedInputs.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.Ordinal),
            occurredAt,
            stimulusInput: payload.SeedStimulusInput,
            triggerNodeId: payload.SeedTriggerNodeId,
            triggerMetadata: payload.SeedTriggerMetadata);
    }

    private static void ValidateTerminalCheckpointStatus(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        ActivityExecutionState state)
    {
        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.ActivityCancelled) &&
            state.Status != ActivityExecutionStatus.Cancelled)
            throw new InvalidOperationException($"Checkpoint scheduler work item '{workItem.WorkItemId}' cannot commit '{RuntimeCheckpointNames.ActivityCancelled}' for activity execution '{state.Execution.ActivityExecutionId}' with status '{state.Status}'.");

        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.ActivityRecovered) &&
            state.Status != ActivityExecutionStatus.Recovered)
            throw new InvalidOperationException($"Checkpoint scheduler work item '{workItem.WorkItemId}' cannot commit '{RuntimeCheckpointNames.ActivityRecovered}' for activity execution '{state.Execution.ActivityExecutionId}' with status '{state.Status}'.");
    }

    private static RuntimeStateChange<WorkflowExecutionState>? BuildWorkflowExecutionStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt,
        WorkflowExecutionState? priorWorkflowState,
        WorkflowExecutable? executable,
        PersistenceMode? persistenceMode)
    {
        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowStarted))
            return BuildWorkflowStartedStateChange(workItem, payload, occurredAt, priorWorkflowState, executable, persistenceMode);

        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowCompleted))
            return BuildWorkflowCompletedStateChange(workItem, payload, occurredAt, priorWorkflowState);

        return null;
    }

    private static RuntimeStateChange<WorkflowExecutionState> BuildWorkflowStartedStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt,
        WorkflowExecutionState? priorWorkflowState,
        WorkflowExecutable? executable,
        PersistenceMode? persistenceMode)
    {
        var startedAt = ReadWorkflowStartedAt(workItem) ?? occurredAt;
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            PinnedExecutable: payload.PinnedExecutable,
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: startedAt,
            StartedAt: startedAt,
            UpdatedAt: occurredAt,
            CompletedAt: null,
            CorrelationId: priorWorkflowState?.CorrelationId ?? payload.CorrelationId,
            ParentWorkflowExecutionId: priorWorkflowState?.ParentWorkflowExecutionId ?? payload.ParentWorkflowExecutionId,
            TenantId: priorWorkflowState?.TenantId ?? payload.TenantId,
            SystemMetadata: RuntimeModelMetadata.Snapshot(PreserveSystemMetadata(RecordPersistenceMode(new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId
            }, persistenceMode), priorWorkflowState, workItem)))
        {
            RunKind = priorWorkflowState?.RunKind ?? payload.RunKind,
            PinnedSource = priorWorkflowState?.PinnedSource ?? payload.PinnedSource,
            Partition = priorWorkflowState?.Partition ?? payload.Partition,
            Authority = priorWorkflowState?.Authority ?? payload.Authority,
            DispatchNestingDepth = priorWorkflowState?.DispatchNestingDepth ?? payload.DispatchNestingDepth,
            TestScope = priorWorkflowState?.TestScope ?? payload.TestScope,
            RootVariableFrame = priorWorkflowState?.RootVariableFrame ?? CreateRootVariableFrame(workItem.WorkflowExecutionId, payload.SeedVariables, executable)
        };

        return NewWorkflowExecutionStateChange(workItem, payload, state);
    }

    // Carries the workflow instance name (#260) forward across a checkpoint rebuild. The completion/running
    // builders rebuild SystemMetadata from scratch, so without this a SetName assignment folded into an
    // activity-completed checkpoint would be wiped by the subsequent workflow-completed checkpoint — mirroring
    // how the dedicated CorrelationId field is carried forward via priorWorkflowState.
    private static Dictionary<string, string> PreserveSystemMetadata(
        Dictionary<string, string> metadata,
        WorkflowExecutionState? priorWorkflowState,
        RuntimeSchedulerWorkItem? workItem = null)
    {
        if (priorWorkflowState?.SystemMetadata.TryGetValue(RuntimeMetadataKeys.InstanceName, out var instanceName) == true)
            metadata[RuntimeMetadataKeys.InstanceName] = instanceName;
        if (priorWorkflowState?.SystemMetadata.TryGetValue(RuntimeMetadataKeys.SourceReferenceId, out var existingReferenceId) == true)
            metadata[RuntimeMetadataKeys.SourceReferenceId] = existingReferenceId;
        else if (workItem?.CommandMetadata.TryGetValue(RuntimeMetadataKeys.SourceReferenceId, out var sourceReferenceId) == true)
            metadata[RuntimeMetadataKeys.SourceReferenceId] = sourceReferenceId;

        // Carry the run's recorded persistence mode forward across every state rebuild that does not resolve it itself
        // (e.g. the workflow-completed transition), unless this rebuild is recording a freshly resolved mode.
        if (!metadata.ContainsKey(RuntimeMetadataKeys.PersistenceMode) &&
            priorWorkflowState?.SystemMetadata.TryGetValue(RuntimeMetadataKeys.PersistenceMode, out var persistenceMode) == true)
            metadata[RuntimeMetadataKeys.PersistenceMode] = persistenceMode;

        return metadata;
    }

    // Every drain runs in a session for its run, whatever its mode, so the session's mode is the one the run runs in.
    private PersistenceMode? DrainModeOf(string workflowExecutionId) =>
        _sessionAccessor?.Current is { } session && StringComparer.Ordinal.Equals(session.WorkflowExecutionId, workflowExecutionId)
            ? session.Mode
            : null;

    // Records the mode the drain runs the run in on the workflow-started system metadata. Absent only when the handler is
    // driven outside a drain, where no session gives a mode.
    private static Dictionary<string, string> RecordPersistenceMode(
        Dictionary<string, string> metadata,
        PersistenceMode? persistenceMode)
    {
        if (persistenceMode is { } mode)
            metadata[RuntimeMetadataKeys.PersistenceMode] = mode.ToString();

        return metadata;
    }

    private static RuntimeStateChange<WorkflowExecutionState> BuildWorkflowCompletedStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt,
        WorkflowExecutionState? priorWorkflowState)
    {
        var startedAt = ReadWorkflowStartedAt(workItem) ?? priorWorkflowState?.StartedAt;
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            PinnedExecutable: payload.PinnedExecutable,
            Status: WorkflowExecutionStatus.Completed,
            SubStatus: null,
            CreatedAt: priorWorkflowState?.CreatedAt ?? startedAt ?? occurredAt,
            StartedAt: startedAt,
            UpdatedAt: occurredAt,
            CompletedAt: occurredAt,
            CorrelationId: priorWorkflowState?.CorrelationId,
            ParentWorkflowExecutionId: priorWorkflowState?.ParentWorkflowExecutionId,
            TenantId: priorWorkflowState?.TenantId,
            SystemMetadata: RuntimeModelMetadata.Snapshot(PreserveSystemMetadata(new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId
            }, priorWorkflowState)))
        {
            RunKind = priorWorkflowState?.RunKind ?? payload.RunKind,
            PinnedSource = priorWorkflowState?.PinnedSource ?? payload.PinnedSource,
            Partition = priorWorkflowState?.Partition ?? payload.Partition,
            Authority = priorWorkflowState?.Authority ?? payload.Authority,
            DispatchNestingDepth = priorWorkflowState?.DispatchNestingDepth ?? payload.DispatchNestingDepth,
            TestScope = priorWorkflowState?.TestScope ?? payload.TestScope,
            RootVariableFrame = priorWorkflowState?.RootVariableFrame is { } rootFrame
                ? rootFrame.Status == VariableFrameStatus.Active ? rootFrame.Close(rootFrame.Revision) : rootFrame
                : null
        };

        return NewWorkflowExecutionStateChange(workItem, payload, state);
    }

    private static DateTimeOffset? ReadWorkflowStartedAt(RuntimeSchedulerWorkItem workItem)
    {
        if (!workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.WorkflowStartedAt, out var value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var startedAt)
            ? startedAt
            : null;
    }

    private static RuntimeStateChange<WorkflowExecutionState> NewWorkflowExecutionStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        WorkflowExecutionState state) =>
        new(
            StateId: workItem.WorkflowExecutionId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: state,
            Metadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CheckpointReason] = payload.Reason
            }));

    private static VariableFrameState CreateRootVariableFrame(
        string workflowExecutionId,
        IReadOnlyDictionary<string, JsonElement> seedVariables,
        WorkflowExecutable? executable)
    {
        if (executable is null)
            throw new InvalidOperationException($"Workflow execution '{workflowExecutionId}' cannot activate its canonical root variable frame without the pinned executable.");

        // The root frame declares exactly the workflow-scope variables (state.Variables compiled into the
        // executable, #972). The root ACTIVITY's structure variables are a normal container scope owned by the
        // root node itself — they are NOT folded into the workflow scope here.
        var projector = new RuntimeVariableDeclarationProjector();
        var declarations = projector.ProjectDeclarations(executable.WorkflowVariables);
        var initial = projector.ProjectInitialValues(executable.WorkflowVariables);
        var values = new Dictionary<string, ValueEnvelope>(initial, StringComparer.Ordinal);
        foreach (var (referenceKey, declaration) in declarations)
        {
            if (!seedVariables.TryGetValue(referenceKey, out var seed) &&
                !seedVariables.TryGetValue(declaration.Name, out seed))
                continue;

            var declared = initial[referenceKey];
            values[referenceKey] = seed.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? ValueEnvelope.Null(declared.Type, declared.Policy)
                : ValueEnvelope.Inline(declared.Type, seed, declared.Policy);
        }

        return new VariableFrameFactory().CreateRoot(
            workflowExecutionId,
            VariableReference.WorkflowScopeId,
            values);
    }

    private static RuntimeCheckpointCommandPayload DeserializeCheckpointPayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "Checkpoint scheduler work item requires a checkpoint payload.",
            resolvedToNullMessage: "Checkpoint scheduler work item payload resolved to null.",
            invalidPayloadMessage: "Checkpoint scheduler work item payload is not a valid checkpoint payload.",
            deserialize: static (_, payload) => payload.Deserialize<RuntimeCheckpointCommandPayload>(),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException or RuntimeCheckpointCommandPayloadValidationException);
}

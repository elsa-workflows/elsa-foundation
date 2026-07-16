using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Executes graph-visible engine value operations without activating CLR activities or creating activity DI scopes.
/// </summary>
public sealed class WorkflowIntrinsicExecutor(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IRuntimeInputBindingResolver inputBindingResolver,
    IDurableValueStateStore durableValueStateStore,
    IRuntimeActivityOutputRegister activityOutputRegister,
    IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
    TimeProvider timeProvider)
{
    public async ValueTask<RuntimeCheckpointCommit> ExecuteAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startWorkItem);
        ArgumentNullException.ThrowIfNull(startPayload);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(intrinsicState);
        cancellationToken.ThrowIfCancellationRequested();

        if (node.IntrinsicKind is null)
            throw new InvalidOperationException($"Executable node '{node.ExecutableNodeId}' is not an engine intrinsic.");
        if (node.ActivityContract is not null)
            throw new InvalidOperationException($"Executable node '{node.ExecutableNodeId}' cannot execute as both a CLR activity and an intrinsic.");
        if (intrinsicState.Status != ActivityExecutionStatus.Scheduled)
            throw new InvalidOperationException($"Intrinsic execution '{intrinsicState.InvocationId}' must be Scheduled before execution; current status is '{intrinsicState.Status}'.");

        var intrinsicKind = node.IntrinsicKind.Value;
        return intrinsicKind switch
        {
            WorkflowIntrinsicKind.Set => await ExecuteSetAsync(
                startWorkItem,
                startPayload,
                executable,
                node,
                intrinsicState,
                serviceProvider,
                cancellationToken),
            WorkflowIntrinsicKind.Merge or WorkflowIntrinsicKind.Reduce =>
                throw new NotSupportedException($"Workflow intrinsic '{intrinsicKind}' requires its deterministic operation contract before it can execute."),
            _ => throw new NotSupportedException($"Workflow intrinsic '{intrinsicKind}' is not implemented by the value-flow executor.")
        };
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteSetAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var target = node.IntrinsicVariable
            ?? throw new InvalidOperationException($"Set intrinsic '{node.ExecutableNodeId}' has no variable target.");
        if (!node.InputBindings.TryGetValue(WorkflowIntrinsicInputKeys.Value, out var valueBinding))
            throw new InvalidOperationException($"Set intrinsic '{node.ExecutableNodeId}' has no '{WorkflowIntrinsicInputKeys.Value}' binding.");

        var workflowState = await workflowExecutionStateStore.FindAsync(startWorkItem.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Set intrinsic '{node.ExecutableNodeId}' references missing workflow execution '{startWorkItem.WorkflowExecutionId}'.");
        var runtimeView = await activityExecutionStateStore.ListAsync(startWorkItem.WorkflowExecutionId, cancellationToken);
        var frameOwner = ResolveVisibleFrame(workflowState, intrinsicState, runtimeView, target.DeclaringScopeId);
        var value = await MaterializeValueAsync(
            valueBinding,
            workflowState,
            intrinsicState,
            executable,
            runtimeView,
            serviceProvider,
            cancellationToken);
        ValidateAssignment(frameOwner.Frame, target.VariableKey, valueBinding, value, node.ExecutableNodeId);

        var changedFrame = frameOwner.Frame.Set(target.VariableKey, value, frameOwner.Frame.Revision);
        var occurredAt = timeProvider.GetUtcNow();
        var completedMetadata = intrinsicState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        completedMetadata[RuntimeMetadataKeys.StartReason] = startPayload.Reason;
        completedMetadata[RuntimeMetadataKeys.StartSchedulerWorkItemId] = startWorkItem.WorkItemId;
        var completedState = intrinsicState with
        {
            Status = ActivityExecutionStatus.Completed,
            StartedAt = occurredAt,
            CompletedAt = occurredAt,
            Attempts = intrinsicState.Attempts ?? [],
            Metadata = RuntimeModelMetadata.Snapshot(completedMetadata)
        };
        var metadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = startWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = startWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = WorkflowIntrinsicKind.Set.ToString(),
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = intrinsicState.InvocationId,
            [RuntimeMetadataKeys.ExecutableNodeId] = node.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = executable.Identity.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = executable.Identity.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = executable.Identity.ArtifactHash,
            ["runtime.variableFrameId"] = changedFrame.FrameId,
            ["runtime.variableFrameRevision"] = changedFrame.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["runtime.variableKey"] = target.VariableKey
        });
        var checkpointId = $"checkpoint:{startWorkItem.WorkItemId}:intrinsic:{intrinsicState.InvocationId}";
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            completedState,
            checkpointId,
            occurredAt,
            metadata: metadata,
            cancellationToken: cancellationToken);
        var completionWorkItem = NewCompletionWorkItem(startWorkItem, startPayload, completedState, occurredAt);
        var activityChanges = new List<RuntimeStateChange<ActivityExecutionState>>
        {
            new(intrinsicState.InvocationId, RuntimeStateChangeOperation.Upsert, completedState, metadata)
        };
        RuntimeStateChange<WorkflowExecutionState>? workflowChange = null;

        if (frameOwner.WorkflowOwned)
        {
            workflowChange = new RuntimeStateChange<WorkflowExecutionState>(
                workflowState.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                workflowState with { RootVariableFrame = changedFrame, UpdatedAt = occurredAt },
                metadata);
        }
        else
        {
            var owner = frameOwner.ActivityOwner!;
            activityChanges.Add(new RuntimeStateChange<ActivityExecutionState>(
                owner.InvocationId,
                RuntimeStateChangeOperation.Upsert,
                owner with { VariableFrame = changedFrame },
                metadata));
        }

        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{startWorkItem.WorkItemId}:intrinsic:{intrinsicState.InvocationId}",
            Checkpoint: new RuntimeCheckpoint(
                checkpointId,
                RuntimeCheckpointNames.IntrinsicCompleted,
                startWorkItem.WorkflowExecutionId,
                occurredAt,
                [intrinsicState.InvocationId],
                metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowChange,
                scheduler: null,
                activityChanges,
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        intrinsicState.InvocationId,
                        RuntimeStateChangeOperation.Upsert,
                        inspection,
                        metadata)
                ]),
            PostCommitIntents:
            [
                SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                    startWorkItem,
                    intrinsicState.InvocationId,
                    completionWorkItem,
                    occurredAt)
            ],
            Metadata: metadata);
    }

    private async ValueTask<ValueEnvelope> MaterializeValueAsync(
        RuntimeInputBinding binding,
        WorkflowExecutionState workflowState,
        ActivityExecutionState intrinsicState,
        WorkflowExecutable executable,
        IReadOnlyCollection<ActivityExecutionState> runtimeView,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var durableValues = await durableValueStateStore.ListAsync(workflowState.WorkflowExecutionId, cancellationToken);
        var projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
        var frameEnvelopes = BuildVisibleFrameEnvelopes(workflowState, intrinsicState, runtimeView);
        foreach (var (address, value) in projections.VariableEnvelopes)
            frameEnvelopes.TryAdd(address, value);

        var context = new RuntimeInputBindingResolutionContext(
            workflowState.WorkflowExecutionId,
            intrinsicState.InvocationId,
            durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
            activityOutputRegister,
            serviceProvider,
            projections.WorkflowVariables,
            projections.WorkflowInputs,
            projections.ActivityOutputValues,
            consumerInvocation: intrinsicState,
            runtimeView: runtimeView,
            executable: executable,
            workflowInputEnvelopes: projections.WorkflowInputEnvelopes,
            variableEnvelopes: frameEnvelopes);
        var resolved = inputBindingResolver.Resolve(binding, context);
        if (resolved.Source == RuntimeInputBindingSource.Expression)
            throw new NotSupportedException($"Set intrinsic '{intrinsicState.Execution.ExecutableNodeId}' cannot evaluate portable expressions until the explicit-parameter expression executor is available.");
        var source = resolved.Envelope
            ?? throw new InvalidOperationException($"Set intrinsic '{intrinsicState.Execution.ExecutableNodeId}' resolved '{binding.InputName}' without its source value envelope.");
        if (source.Presence == ValuePresence.Absent)
            throw new InvalidOperationException($"Set intrinsic '{intrinsicState.Execution.ExecutableNodeId}' cannot assign an absent value.");
        if (source.Policy.Lifecycle == DurableValueLifecycle.None || !binding.EffectivePolicy.Satisfies(source.Policy))
            throw new InvalidOperationException($"Set intrinsic '{intrinsicState.Execution.ExecutableNodeId}' cannot persist '{binding.InputName}' without preserving its source protection policy.");

        return new ValueEnvelope(binding.TargetType, source.Presence, source.InlineValue, source.ExternalReference, binding.EffectivePolicy);
    }

    private static FrameOwner ResolveVisibleFrame(
        WorkflowExecutionState workflowState,
        ActivityExecutionState intrinsicState,
        IReadOnlyCollection<ActivityExecutionState> runtimeView,
        string declaringScopeId)
    {
        var ancestors = new List<ActivityExecutionState>();
        var byId = runtimeView.ToDictionary(state => state.InvocationId, StringComparer.Ordinal);
        var ancestorId = intrinsicState.ParentActivityExecutionId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (ancestorId is not null && visited.Add(ancestorId) && byId.TryGetValue(ancestorId, out var ancestor))
        {
            ancestors.Add(ancestor);
            ancestorId = ancestor.ParentActivityExecutionId;
        }

        var activityOwner = ancestors.FirstOrDefault(state =>
            state.VariableFrame is { Status: VariableFrameStatus.Active } frame &&
            StringComparer.Ordinal.Equals(frame.ScopeId, declaringScopeId));
        if (activityOwner?.VariableFrame is { } activityFrame)
            return new FrameOwner(activityFrame, WorkflowOwned: false, activityOwner);

        if (workflowState.RootVariableFrame is { Status: VariableFrameStatus.Active } root &&
            StringComparer.Ordinal.Equals(root.ScopeId, declaringScopeId))
            return new FrameOwner(root, WorkflowOwned: true, ActivityOwner: null);

        throw new InvalidOperationException(
            $"Variable scope '{declaringScopeId}' is not visible to intrinsic execution '{intrinsicState.InvocationId}'.");
    }

    private static Dictionary<RuntimeVariableValueAddress, ValueEnvelope> BuildVisibleFrameEnvelopes(
        WorkflowExecutionState workflowState,
        ActivityExecutionState intrinsicState,
        IReadOnlyCollection<ActivityExecutionState> runtimeView)
    {
        var result = new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>();
        if (workflowState.RootVariableFrame is { Status: VariableFrameStatus.Active } root)
            AddFrame(root, result);

        var byId = runtimeView.ToDictionary(state => state.InvocationId, StringComparer.Ordinal);
        var ancestors = new Stack<ActivityExecutionState>();
        var ancestorId = intrinsicState.ParentActivityExecutionId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (ancestorId is not null && visited.Add(ancestorId) && byId.TryGetValue(ancestorId, out var ancestor))
        {
            ancestors.Push(ancestor);
            ancestorId = ancestor.ParentActivityExecutionId;
        }

        foreach (var ancestor in ancestors)
        {
            if (ancestor.VariableFrame is { Status: VariableFrameStatus.Active } frame)
                AddFrame(frame, result);
        }

        return result;
    }

    private static void AddFrame(
        VariableFrameState frame,
        IDictionary<RuntimeVariableValueAddress, ValueEnvelope> values)
    {
        foreach (var (key, value) in frame.Values)
            values[new RuntimeVariableValueAddress(frame.ScopeId, key)] = value;
    }

    private static void ValidateAssignment(
        VariableFrameState frame,
        string variableKey,
        RuntimeInputBinding binding,
        ValueEnvelope value,
        string nodeId)
    {
        if (!frame.Values.TryGetValue(variableKey, out var current))
            throw new InvalidOperationException($"Set intrinsic '{nodeId}' targets undeclared variable '{variableKey}' in frame '{frame.FrameId}'.");
        if ((!StringComparer.Ordinal.Equals(current.Type.Alias, "Elsa.Any") && !SameType(current.Type, binding.TargetType)) ||
            !SameType(binding.TargetType, value.Type))
            throw new InvalidOperationException($"Set intrinsic '{nodeId}' value type '{value.Type.Alias}' does not match variable '{variableKey}' type '{current.Type.Alias}'.");
        if (!value.Policy.Satisfies(current.Policy))
            throw new InvalidOperationException($"Set intrinsic '{nodeId}' would downgrade the protection policy of variable '{variableKey}'.");
    }

    private static bool SameType(ValueTypeDescriptor left, ValueTypeDescriptor right) =>
        StringComparer.Ordinal.Equals(left.Alias, right.Alias) &&
        left.CollectionKind == right.CollectionKind &&
        left.SchemaVersion == right.SchemaVersion &&
        StringComparer.Ordinal.Equals(left.Schema?.GetRawText(), right.Schema?.GetRawText());

    private static RuntimeSchedulerWorkItem NewCompletionWorkItem(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        ActivityExecutionState completedState,
        DateTimeOffset occurredAt)
    {
        var payload = new RuntimeCompleteActivityCommandPayload(
            startPayload.PinnedExecutable,
            startPayload.ExecutableNodeId,
            startPayload.ActivityExecutionId,
            completedState.ParentActivityExecutionId,
            completedState.BranchId,
            ["Done"],
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);
        return new RuntimeSchedulerWorkItem(
            $"{startWorkItem.WorkItemId}:complete:{startPayload.ActivityExecutionId}",
            startWorkItem.WorkflowExecutionId,
            $"{startWorkItem.CommandId}:complete:{startPayload.ActivityExecutionId}",
            WorkflowExecutionCommandKind.CompleteActivity,
            startWorkItem.EnvelopeId,
            $"{startWorkItem.IdempotencyKey}:complete:{startPayload.ActivityExecutionId}",
            occurredAt,
            occurredAt,
            startWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            JsonSerializer.SerializeToElement(payload),
            startWorkItem.CommandMetadata,
            startWorkItem.EnvelopeMetadata);
    }

    private sealed record FrameOwner(
        VariableFrameState Frame,
        bool WorkflowOwned,
        ActivityExecutionState? ActivityOwner);
}

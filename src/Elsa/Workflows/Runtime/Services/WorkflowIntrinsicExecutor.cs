using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
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
    IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
    TimeProvider timeProvider,
    IExternalPayloadStore? externalPayloadStore = null,
    IPortableExpressionEvaluator? portableExpressionEvaluator = null)
{
    private readonly RuntimePortableExpressionEvaluator _portableExpressionEvaluator = new(portableExpressionEvaluator, externalPayloadStore);
    private readonly RuntimeExternalEnvelopeStorage _externalEnvelopeStorage = new(externalPayloadStore);

    public async ValueTask<RuntimeCheckpointCommit> ExecuteAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
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
            WorkflowIntrinsicKind.Set or WorkflowIntrinsicKind.Merge or WorkflowIntrinsicKind.Reduce => await ExecuteVariableWriteAsync(
                startWorkItem,
                startPayload,
                executable,
                node,
                intrinsicState,
                intrinsicKind,
                cancellationToken),
            WorkflowIntrinsicKind.Return or WorkflowIntrinsicKind.Control => await ExecuteResultAsync(
                startWorkItem,
                startPayload,
                executable,
                node,
                intrinsicState,
                intrinsicKind,
                cancellationToken),
            WorkflowIntrinsicKind.SetCorrelationId or WorkflowIntrinsicKind.SetInstanceName => await ExecuteIdentityEffectAsync(
                startWorkItem,
                startPayload,
                executable,
                node,
                intrinsicState,
                intrinsicKind,
                cancellationToken),
            WorkflowIntrinsicKind.SetOutput => await ExecuteSetOutputAsync(
                startWorkItem,
                startPayload,
                executable,
                node,
                intrinsicState,
                cancellationToken),
            WorkflowIntrinsicKind.Finish => await ExecuteFinishAsync(
                startWorkItem,
                startPayload,
                executable,
                node,
                intrinsicState,
                cancellationToken),
            _ => throw new NotSupportedException($"Workflow intrinsic '{intrinsicKind}' is not implemented by the value-flow executor.")
        };
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteVariableWriteAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        WorkflowIntrinsicKind intrinsicKind,
        CancellationToken cancellationToken)
    {
        var target = node.IntrinsicVariable
            ?? throw new InvalidOperationException($"{intrinsicKind} intrinsic '{node.ExecutableNodeId}' has no variable target.");
        if (!node.InputBindings.TryGetValue(WorkflowIntrinsicInputKeys.Value, out var valueBinding))
            throw new InvalidOperationException($"{intrinsicKind} intrinsic '{node.ExecutableNodeId}' has no '{WorkflowIntrinsicInputKeys.Value}' binding.");

        var workflowState = await workflowExecutionStateStore.FindAsync(startWorkItem.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"{intrinsicKind} intrinsic '{node.ExecutableNodeId}' references missing workflow execution '{startWorkItem.WorkflowExecutionId}'.");
        var runtimeView = await activityExecutionStateStore.ListAllAsync(startWorkItem.WorkflowExecutionId, cancellationToken);
        var frameOwner = ResolveVisibleFrame(workflowState, intrinsicState, runtimeView, target.DeclaringScopeId);
        var value = await MaterializeValueAsync(
            valueBinding,
            workflowState,
            intrinsicState,
            executable,
            runtimeView,
            cancellationToken);
        ValidateAssignment(frameOwner.Frame, target.VariableKey, valueBinding, value, node.ExecutableNodeId);

        var changedFrame = frameOwner.Frame.Set(target.VariableKey, value, frameOwner.Frame.Revision);
        WorkflowExecutionState? changedWorkflow = null;
        ActivityExecutionState? changedActivityOwner = null;
        if (frameOwner.WorkflowOwned)
            changedWorkflow = workflowState with { RootVariableFrame = changedFrame };
        else
            changedActivityOwner = frameOwner.ActivityOwner! with { VariableFrame = changedFrame };

        return await CompleteIntrinsicAsync(
            startWorkItem,
            startPayload,
            executable,
            node,
            intrinsicState,
            intrinsicKind,
            completionResult: null,
            ["Done"],
            changedWorkflow,
            changedActivityOwner,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime.variableFrameId"] = changedFrame.FrameId,
                ["runtime.variableFrameRevision"] = changedFrame.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["runtime.variableKey"] = target.VariableKey
            },
            cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteResultAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        WorkflowIntrinsicKind intrinsicKind,
        CancellationToken cancellationToken)
    {
        var inputKey = intrinsicKind == WorkflowIntrinsicKind.Control
            ? WorkflowIntrinsicInputKeys.Outcome
            : WorkflowIntrinsicInputKeys.Value;
        if (!node.InputBindings.TryGetValue(inputKey, out var binding))
            throw new InvalidOperationException($"{intrinsicKind} intrinsic '{node.ExecutableNodeId}' has no '{inputKey}' binding.");

        var workflowState = await workflowExecutionStateStore.FindAsync(startWorkItem.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"{intrinsicKind} intrinsic '{node.ExecutableNodeId}' references missing workflow execution '{startWorkItem.WorkflowExecutionId}'.");
        var runtimeView = await activityExecutionStateStore.ListAllAsync(startWorkItem.WorkflowExecutionId, cancellationToken);
        var materialized = await MaterializeValueAsync(
            binding,
            workflowState,
            intrinsicState,
            executable,
            runtimeView,
            cancellationToken);

        string outcome;
        ValueEnvelope result;
        if (intrinsicKind == WorkflowIntrinsicKind.Control)
        {
            if (materialized.Presence != ValuePresence.Present ||
                materialized.InlineValue is not { ValueKind: JsonValueKind.String } outcomeValue ||
                string.IsNullOrWhiteSpace(outcomeValue.GetString()))
            {
                throw new InvalidOperationException($"Control intrinsic '{node.ExecutableNodeId}' requires a non-blank literal outcome key.");
            }

            outcome = outcomeValue.GetString()!;
            var unitType = new ValueTypeDescriptor("Elsa.Activities.Runtime.Core.Models.ActivityUnit");
            result = ValueEnvelope.Inline(
                unitType,
                JsonSerializer.SerializeToElement(ActivityUnit.Value),
                ValueProtectionPolicy.InstanceInline);
        }
        else
        {
            outcome = "Done";
            result = materialized;
        }

        return await CompleteIntrinsicAsync(
            startWorkItem,
            startPayload,
            executable,
            node,
            intrinsicState,
            intrinsicKind,
            result,
            [outcome],
            changedWorkflow: null,
            changedActivityOwner: null,
            metadataAdditions: null,
            cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteIdentityEffectAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        WorkflowIntrinsicKind intrinsicKind,
        CancellationToken cancellationToken)
    {
        var value = await MaterializeRequiredInputAsync(
            WorkflowIntrinsicInputKeys.Value,
            startWorkItem,
            executable,
            node,
            intrinsicState,
            cancellationToken);
        var assigned = ReadOptionalString(value, intrinsicKind, node.ExecutableNodeId);
        var occurredAt = timeProvider.GetUtcNow();
        var workflowState = await workflowExecutionStateStore.FindAsync(startWorkItem.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"{intrinsicKind} intrinsic '{node.ExecutableNodeId}' references missing workflow execution '{startWorkItem.WorkflowExecutionId}'.");
        var setsCorrelation = intrinsicKind == WorkflowIntrinsicKind.SetCorrelationId;
        var changedWorkflow = workflowState with
        {
            CorrelationId = setsCorrelation ? assigned : workflowState.CorrelationId,
            SystemMetadata = setsCorrelation ? workflowState.SystemMetadata : ApplyInstanceName(workflowState.SystemMetadata, assigned)
        };
        var durableChanges = RuntimeWorkflowStateSeed.BuildIdentityChanges(
            startWorkItem.WorkflowExecutionId,
            setsCorrelation,
            setsCorrelation ? assigned : null,
            !setsCorrelation,
            setsCorrelation ? null : assigned,
            occurredAt);

        return await CompleteIntrinsicAsync(
            startWorkItem,
            startPayload,
            executable,
            node,
            intrinsicState,
            intrinsicKind,
            UnitResult(),
            ["Done"],
            changedWorkflow,
            changedActivityOwner: null,
            metadataAdditions: null,
            cancellationToken,
            durableChanges);
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteSetOutputAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        CancellationToken cancellationToken)
    {
        var nameBinding = node.InputBindings[WorkflowIntrinsicInputKeys.Name];
        if (nameBinding.Source != RuntimeInputBindingSource.Literal || nameBinding.Literal is not { } nameLiteral)
            throw new InvalidOperationException($"SetOutput intrinsic '{node.ExecutableNodeId}' requires a literal output name.");
        var outputName = ReadRequiredString(nameLiteral, WorkflowIntrinsicKind.SetOutput, node.ExecutableNodeId);
        var value = await MaterializeRequiredInputAsync(
            WorkflowIntrinsicInputKeys.Value,
            startWorkItem,
            executable,
            node,
            intrinsicState,
            cancellationToken);
        var durableChange = NewWorkflowOutputChange(
            startWorkItem.WorkflowExecutionId,
            intrinsicState.InvocationId,
            outputName,
            value,
            timeProvider.GetUtcNow());

        return await CompleteIntrinsicAsync(
            startWorkItem,
            startPayload,
            executable,
            node,
            intrinsicState,
            WorkflowIntrinsicKind.SetOutput,
            UnitResult(),
            ["Done"],
            changedWorkflow: null,
            changedActivityOwner: null,
            metadataAdditions: null,
            cancellationToken,
            [durableChange]);
    }

    private async ValueTask<RuntimeCheckpointCommit> ExecuteFinishAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        CancellationToken cancellationToken)
    {
        var outcomeBinding = node.InputBindings[WorkflowIntrinsicInputKeys.Outcome];
        if (outcomeBinding.Source != RuntimeInputBindingSource.Literal ||
            outcomeBinding.Literal is not { } literal)
            throw new InvalidOperationException($"Finish intrinsic '{node.ExecutableNodeId}' requires a literal outcome key.");
        var outcome = ReadRequiredString(literal, WorkflowIntrinsicKind.Finish, node.ExecutableNodeId);
        var workflowState = await workflowExecutionStateStore.FindAsync(startWorkItem.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Finish intrinsic '{node.ExecutableNodeId}' references missing workflow execution '{startWorkItem.WorkflowExecutionId}'.");
        var occurredAt = timeProvider.GetUtcNow();
        var changedWorkflow = RuntimeContainerScopeService.CloseRootFrame(workflowState with
        {
            Status = WorkflowExecutionStatus.Completed,
            SubStatus = null,
            CompletedAt = occurredAt
        });

        return await CompleteIntrinsicAsync(
            startWorkItem,
            startPayload,
            executable,
            node,
            intrinsicState,
            WorkflowIntrinsicKind.Finish,
            UnitResult(),
            [outcome],
            changedWorkflow,
            changedActivityOwner: null,
            metadataAdditions: null,
            cancellationToken,
            durableValueChanges: null,
            terminatesWorkflow: true);
    }

    private async ValueTask<RuntimeCheckpointCommit> CompleteIntrinsicAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        WorkflowIntrinsicKind intrinsicKind,
        ValueEnvelope? completionResult,
        IReadOnlyCollection<string> outcomeNames,
        WorkflowExecutionState? changedWorkflow,
        ActivityExecutionState? changedActivityOwner,
        IReadOnlyDictionary<string, string>? metadataAdditions,
        CancellationToken cancellationToken,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? durableValueChanges = null,
        bool terminatesWorkflow = false)
    {
        var occurredAt = timeProvider.GetUtcNow();
        var completedMetadata = intrinsicState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        completedMetadata[RuntimeMetadataKeys.StartReason] = startPayload.Reason;
        completedMetadata[RuntimeMetadataKeys.StartSchedulerWorkItemId] = startWorkItem.WorkItemId;
        var completion = completionResult is null
            ? null
            : new ActivityCompletion(
                intrinsicState.InvocationId,
                $"intrinsic:{intrinsicState.InvocationId}",
                completionResult,
                outcomeNames.Single(),
                occurredAt,
                $"intrinsic:{intrinsicKind}:1.0.0");
        var completedState = RuntimeContainerScopeService.CloseOwnedFrames(intrinsicState with
        {
            Status = ActivityExecutionStatus.Completed,
            StartedAt = occurredAt,
            CompletedAt = occurredAt,
            Attempts = intrinsicState.Attempts ?? [],
            Completion = completion,
            Metadata = RuntimeModelMetadata.Snapshot(completedMetadata)
        });
        var metadataValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = startWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = startWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = intrinsicKind.ToString(),
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = intrinsicState.InvocationId,
            [RuntimeMetadataKeys.ExecutableNodeId] = node.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = executable.Identity.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = executable.Identity.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = executable.Identity.ArtifactHash
        };
        foreach (var addition in metadataAdditions ?? new Dictionary<string, string>())
            metadataValues[addition.Key] = addition.Value;
        var metadata = RuntimeModelMetadata.Snapshot(metadataValues);
        var checkpointName = terminatesWorkflow ? RuntimeCheckpointNames.WorkflowCompleted : RuntimeCheckpointNames.IntrinsicCompleted;
        var checkpointId = $"checkpoint:{startWorkItem.WorkItemId}:{(terminatesWorkflow ? "workflow-completed" : "intrinsic")}:{intrinsicState.InvocationId}";
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            completedState,
            checkpointId,
            occurredAt,
            outcomeNames,
            metadata: metadata,
            cancellationToken: cancellationToken);
        var completionWorkItem = terminatesWorkflow
            ? null
            : NewCompletionWorkItem(startWorkItem, startPayload, completedState, outcomeNames, occurredAt);
        var activityChanges = new List<RuntimeStateChange<ActivityExecutionState>>
        {
            new(intrinsicState.InvocationId, RuntimeStateChangeOperation.Upsert, completedState, metadata)
        };
        if (changedActivityOwner is not null)
        {
            activityChanges.Add(new RuntimeStateChange<ActivityExecutionState>(
                changedActivityOwner.InvocationId,
                RuntimeStateChangeOperation.Upsert,
                changedActivityOwner,
                metadata));
        }

        var workflowChange = changedWorkflow is null
            ? null
            : new RuntimeStateChange<WorkflowExecutionState>(
                changedWorkflow.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                changedWorkflow with { UpdatedAt = occurredAt },
                metadata);
        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{startWorkItem.WorkItemId}:intrinsic:{intrinsicState.InvocationId}",
            Checkpoint: new RuntimeCheckpoint(
                checkpointId,
                checkpointName,
                startWorkItem.WorkflowExecutionId,
                occurredAt,
                [intrinsicState.InvocationId],
                metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowChange,
                scheduler: null,
                activityChanges,
                bookmarks: [],
                durableValues: durableValueChanges ?? [],
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
            PostCommitIntents: completionWorkItem is null
                ? []
                : [SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(startWorkItem, intrinsicState.InvocationId, completionWorkItem, occurredAt)],
            Metadata: metadata);
    }

    private async ValueTask<ValueEnvelope> MaterializeRequiredInputAsync(
        string inputKey,
        RuntimeSchedulerWorkItem startWorkItem,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState intrinsicState,
        CancellationToken cancellationToken)
    {
        if (!node.InputBindings.TryGetValue(inputKey, out var binding))
            throw new InvalidOperationException($"{node.IntrinsicKind} intrinsic '{node.ExecutableNodeId}' has no '{inputKey}' binding.");
        var workflowState = await workflowExecutionStateStore.FindAsync(startWorkItem.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"{node.IntrinsicKind} intrinsic '{node.ExecutableNodeId}' references missing workflow execution '{startWorkItem.WorkflowExecutionId}'.");
        var runtimeView = await activityExecutionStateStore.ListAllAsync(startWorkItem.WorkflowExecutionId, cancellationToken);
        return await MaterializeValueAsync(binding, workflowState, intrinsicState, executable, runtimeView, cancellationToken);
    }

    private static ValueEnvelope UnitResult() => ValueEnvelope.Inline(
        new ValueTypeDescriptor("Elsa.Activities.Runtime.Core.Models.ActivityUnit"),
        JsonSerializer.SerializeToElement(ActivityUnit.Value),
        ValueProtectionPolicy.InstanceInline);

    private static string ReadRequiredString(ValueEnvelope value, WorkflowIntrinsicKind kind, string nodeId) =>
        ReadOptionalString(value, kind, nodeId) is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"{kind} intrinsic '{nodeId}' requires a non-blank string value.");

    private static string? ReadOptionalString(ValueEnvelope value, WorkflowIntrinsicKind kind, string nodeId)
    {
        if (value.Presence == ValuePresence.ExplicitNull)
            return null;
        if (value.Presence != ValuePresence.Present || value.InlineValue is not { ValueKind: JsonValueKind.String } json)
            throw new InvalidOperationException($"{kind} intrinsic '{nodeId}' requires a string or explicit null value.");
        var text = json.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static IReadOnlyDictionary<string, string> ApplyInstanceName(
        IReadOnlyDictionary<string, string> source,
        string? instanceName)
    {
        var metadata = source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (instanceName is null)
            metadata.Remove(RuntimeMetadataKeys.InstanceName);
        else
            metadata[RuntimeMetadataKeys.InstanceName] = instanceName;
        return RuntimeModelMetadata.Snapshot(metadata);
    }

    private static RuntimeStateChange<DurableValueState> NewWorkflowOutputChange(
        string workflowExecutionId,
        string activityExecutionId,
        string outputName,
        ValueEnvelope value,
        DateTimeOffset capturedAt)
    {
        var metadata = new Dictionary<string, string>(value.Policy.Metadata, StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.OutputName] = outputName
        };
        if (value.Policy.IsSensitive)
            metadata[RuntimeMetadataKeys.IsSensitive] = bool.TrueString;
        if (value.Policy.RequiresEncryption)
            metadata[RuntimeMetadataKeys.RequiresEncryption] = bool.TrueString;
        if (value.Policy.RedactionMode is not null)
            metadata[RuntimeMetadataKeys.RedactionMode] = value.Policy.RedactionMode;
        if (value.Policy.RetentionPolicy is not null)
            metadata[RuntimeMetadataKeys.RetentionPolicy] = value.Policy.RetentionPolicy;

        var valueId = $"{RuntimeWorkflowStateSeed.OutputValueIdPrefix}{outputName}";
        var durableValueId = $"durable-{valueId}";
        JsonElement? inline = value.Presence switch
        {
            ValuePresence.Present => value.InlineValue,
            ValuePresence.ExplicitNull when value.Policy.Storage is DurableValueStorage.Inline or DurableValueStorage.Custom =>
                JsonSerializer.SerializeToElement<object?>(null),
            _ => null
        };
        var state = new DurableValueState(
            durableValueId,
            workflowExecutionId,
            valueId,
            new RuntimeValueTypeDescriptor("alias", value.Type.Alias, JsonSerializer.SerializeToElement(value.Type)),
            value.Policy.Lifecycle,
            value.Policy.Storage,
            inline,
            value.ExternalReference,
            activityExecutionId,
            capturedAt,
            RuntimeModelMetadata.Snapshot(metadata));
        return new RuntimeStateChange<DurableValueState>(
            durableValueId,
            RuntimeStateChangeOperation.Upsert,
            state,
            RuntimeModelMetadata.Snapshot(metadata));
    }

    private async ValueTask<ValueEnvelope> MaterializeValueAsync(
        RuntimeInputBinding binding,
        WorkflowExecutionState workflowState,
        ActivityExecutionState intrinsicState,
        WorkflowExecutable executable,
        IReadOnlyCollection<ActivityExecutionState> runtimeView,
        CancellationToken cancellationToken)
    {
        var durableValues = await durableValueStateStore.ListAllDurableValueStatesAsync(workflowState.WorkflowExecutionId, cancellationToken);
        var projections = RuntimeInputBindingStateProjection.ProjectAll(durableValues);
        var frameEnvelopes = BuildVisibleFrameEnvelopes(workflowState, intrinsicState, runtimeView);

        var context = new RuntimeInputBindingResolutionContext(
            workflowState.WorkflowExecutionId,
            intrinsicState.InvocationId,
            consumerInvocation: intrinsicState,
            runtimeView: runtimeView,
            executable: executable,
            workflowInputEnvelopes: projections.WorkflowInputEnvelopes,
            variableEnvelopes: frameEnvelopes);
        var resolved = inputBindingResolver.Resolve(binding, context);
        if (resolved.Source == RuntimeInputBindingSource.Expression)
        {
            var expression = resolved.Expression
                ?? throw new InvalidOperationException($"Intrinsic '{intrinsicState.Execution.ExecutableNodeId}' resolved an expression without its portable definition.");
            if (binding.EffectivePolicy.Lifecycle == DurableValueLifecycle.None)
                throw new InvalidOperationException($"Intrinsic '{intrinsicState.Execution.ExecutableNodeId}' cannot persist transient expression input '{binding.InputName}'.");

            var evaluated = await _portableExpressionEvaluator.EvaluateAsync(
                expression,
                binding.TargetType,
                binding.EffectivePolicy,
                intrinsicState.Execution.ExecutableNodeId,
                binding.InputName,
                context,
                cancellationToken);
            var envelope = evaluated.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? ValueEnvelope.Null(binding.TargetType, evaluated.EffectivePolicy)
                : ValueEnvelope.Inline(binding.TargetType, evaluated.Value, evaluated.EffectivePolicy);
            return await ApplyStorageAsync(
                workflowState.WorkflowExecutionId,
                intrinsicState.InvocationId,
                binding.InputName,
                envelope,
                evaluated.EffectivePolicy,
                evaluated.EffectivePolicy,
                cancellationToken);
        }
        var source = resolved.Envelope
            ?? throw new InvalidOperationException($"Intrinsic '{intrinsicState.Execution.ExecutableNodeId}' resolved '{binding.InputName}' without its source value envelope.");
        if (source.Presence == ValuePresence.Absent)
            throw new InvalidOperationException($"Intrinsic '{intrinsicState.Execution.ExecutableNodeId}' cannot materialize an absent value.");
        if (source.Policy.Lifecycle == DurableValueLifecycle.None)
            throw new InvalidOperationException($"Intrinsic '{intrinsicState.Execution.ExecutableNodeId}' cannot persist transient source '{binding.InputName}'.");
        var combinedPolicy = ValuePolicyCombiner.Combine(
            binding.EffectivePolicy,
            source.Policy,
            $"Intrinsic input '{binding.InputName}' on node '{intrinsicState.Execution.ExecutableNodeId}'");

        var combined = new ValueEnvelope(binding.TargetType, source.Presence, source.InlineValue, source.ExternalReference, combinedPolicy);
        return await ApplyStorageAsync(
            workflowState.WorkflowExecutionId,
            intrinsicState.InvocationId,
            binding.InputName,
            combined,
            source.Policy,
            combinedPolicy,
            cancellationToken);
    }

    private async ValueTask<ValueEnvelope> ApplyStorageAsync(
        string workflowExecutionId,
        string invocationId,
        string valueName,
        ValueEnvelope value,
        ValueProtectionPolicy sourcePolicy,
        ValueProtectionPolicy effectivePolicy,
        CancellationToken cancellationToken)
    {
        return await _externalEnvelopeStorage.RewriteAsync(
            new RuntimeExternalEnvelopeRewriteRequest(
                workflowExecutionId,
                $"intrinsic:{invocationId}:value:{valueName}",
                $"Intrinsic value '{valueName}'",
                value,
                sourcePolicy,
                effectivePolicy),
            cancellationToken);
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

        var readOnlyIteration = ancestors
            .Select(state => state.IterationVariableFrame)
            .Append(intrinsicState.IterationVariableFrame)
            .FirstOrDefault(frame => frame is { Status: VariableFrameStatus.Active } &&
                                     StringComparer.Ordinal.Equals(frame.ScopeId, declaringScopeId));
        if (readOnlyIteration is not null)
            throw new InvalidOperationException($"Iteration variable scope '{declaringScopeId}' is read-only and cannot be targeted by a Set, merge, or reduce intrinsic.");

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
            if (ancestor.IterationVariableFrame is { Status: VariableFrameStatus.Active } iteration)
                AddFrame(iteration, result);
            if (ancestor.VariableFrame is { Status: VariableFrameStatus.Active } frame)
                AddFrame(frame, result);
        }

        if (intrinsicState.IterationVariableFrame is { Status: VariableFrameStatus.Active } currentIteration)
            AddFrame(currentIteration, result);

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
        IReadOnlyCollection<string> outcomeNames,
        DateTimeOffset occurredAt)
    {
        var payload = new RuntimeCompleteActivityCommandPayload(
            startPayload.PinnedExecutable,
            startPayload.ExecutableNodeId,
            startPayload.ActivityExecutionId,
            completedState.ParentActivityExecutionId,
            completedState.BranchId,
            outcomeNames,
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

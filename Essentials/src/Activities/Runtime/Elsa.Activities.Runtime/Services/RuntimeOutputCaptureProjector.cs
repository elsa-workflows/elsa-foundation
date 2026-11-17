using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Projects selected atomic activity-result values into their published durable targets.
/// The returned changes are committed in the same checkpoint as activity completion. Captures that
/// target a workflow-scope variable (#972) are routed to the canonical root variable frame — the
/// single runtime truth for workflow variables — instead of the retired <c>variable:*</c>
/// durable-value channel; the handler folds the returned frame writes into the same commit.
/// </summary>
public sealed class RuntimeOutputCaptureProjector
{
    private readonly IRuntimeDurableValueStorageDriverRegistry _storageDrivers;
    private readonly IRuntimeValueConversionExecutor _valueConversionExecutor;

    public RuntimeOutputCaptureProjector(IRuntimeDurableValueStorageDriverRegistry storageDrivers)
        : this(storageDrivers, new RuntimeValueConversionExecutor())
    {
    }

    public RuntimeOutputCaptureProjector(
        IRuntimeDurableValueStorageDriverRegistry storageDrivers,
        IRuntimeValueConversionExecutor valueConversionExecutor)
    {
        ArgumentNullException.ThrowIfNull(storageDrivers);
        ArgumentNullException.ThrowIfNull(valueConversionExecutor);

        _storageDrivers = storageDrivers;
        _valueConversionExecutor = valueConversionExecutor;
    }

    public async ValueTask<RuntimeOutputCaptureProjection> ProjectAsync(
        string workflowExecutionId,
        string activityExecutionId,
        ExecutableNode node,
        IActivityCompletionTransition transition,
        ActivityCompletionProjection completion,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default,
        ActivityExecutionState? closedScopeBoundary = null,
        ActivityExecutionState? invocation = null,
        VariableWriteSource source = VariableWriteSource.Capture,
        IDurableValueStateStore? durableValueStateStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(completion);

        if (node.OutputCaptures.Count == 0)
            return RuntimeOutputCaptureProjection.Empty;
        var contract = node.ActivityContract
            ?? throw new InvalidOperationException($"Executable node '{node.ExecutableNodeId}' has output captures but no pinned activity contract.");
        var changes = new List<RuntimeStateChange<DurableValueState>>();
        var workflowVariableWrites = new Dictionary<string, RuntimeDurableValueEncoding>(StringComparer.Ordinal);
        var rowWrites = new List<VariableWriteRecord>();
        VariableWriteRecord? rowFault = null;
        foreach (var capture in node.OutputCaptures.Values
                     .Where(item => item.CaptureOnSuccessfulCompletion)
                     .OrderBy(item => item.OutputName, StringComparer.Ordinal))
        {
            if (!contract.Result.Projections.TryGetValue(capture.OutputName, out var projectionContract))
                throw new InvalidOperationException($"Output capture '{capture.OutputName}' is not declared by activity contract '{contract.ActivityTypeKey}'.");
            if (!completion.Projections.TryGetValue(capture.OutputName, out var projected))
                throw new InvalidOperationException($"Activity completion did not produce declared output projection '{capture.OutputName}'.");
            if (projected.Presence == ValuePresence.Absent)
                continue;
            if (capture.Metadata.TryGetValue(RuntimeMetadataKeys.CaptureTargetScopeId, out var targetScopeId))
            {
                var row = await ProjectGraphVariableAsync(
                    workflowExecutionId, activityExecutionId, capture, projectionContract, projected, transition, targetScopeId,
                    closedScopeBoundary, capturedAt, cancellationToken);
                var versioned = await VersionRowAsync(workflowExecutionId, node, row, closedScopeBoundary!, invocation, source, durableValueStateStore,
                    projected.Policy, capturedAt, cancellationToken);
                if (versioned.Record is { } record)
                    rowWrites.Add(record);
                if (versioned.Record?.Outcome == VariableCommitOutcome.LostUpdateFault)
                {
                    // A refused row write refuses the whole completion, as a refused frame write does.
                    rowFault = versioned.Record;
                    break;
                }
                changes.Add(versioned.Row);
                continue;
            }
            var targetsVariable = capture.ValueId.StartsWith(RuntimeWorkflowStateSeed.VariableValueIdPrefix, StringComparison.Ordinal);
            // A result whose policy requires encryption is withheld by the completion projector and
            // holds no value to capture; a present projection whose policy requires encryption, which only a projection
            // built outside the completion projector can carry, is treated the same. Neither reaches a storage driver.
            // A workflow variable has a withheld form: the capture writes the marker into the variable frame, as Set
            // does, and a reader of the variable refuses it with VF-ACT-010. A durable output has none, so the capture
            // is refused.
            if (projected.Presence == ValuePresence.Withheld || projected.HoldsValueRequiringEncryption())
            {
                if (!targetsVariable)
                    throw SecretBindingDiagnostics.WithheldOutputNotCaptured(capture.OutputName);

                // The Withheld presence was handled by the guard above, so when TryWithhold declines, projected is itself a Withheld envelope.
                var withheld = RuntimeEncryptionWithholding.TryWithhold(projected, projected.Policy, out var marker) ? marker : projected;
                workflowVariableWrites[TargetVariableKey(capture, node)] = RuntimeDurableValueEncoding.ForWithheld(withheld);
                continue;
            }

            ValidateDurableCaptureBoundary(capture, projected);

            var value = capture.ConversionPlan is null
                ? StringComparer.Ordinal.Equals(projectionContract.Path, "$")
                    ? transition.Result
                    : projected.Presence == ValuePresence.ExplicitNull
                        ? null
                        : projected.InlineValue
                : ToEncodedValue(_valueConversionExecutor.Convert(projected, capture.ConversionPlan));
            var driver = _storageDrivers.GetRequired(capture.StorageDriverKey);
            var encoding = await driver.EncodeAsync(value, capture.Type, cancellationToken);
            if (capture.Storage != DurableValueStorage.Custom && encoding.Storage != capture.Storage)
            {
                throw new InvalidOperationException(
                    $"Output capture '{capture.OutputName}' declares storage '{capture.Storage}', but driver '{capture.StorageDriverKey}' produced '{encoding.Storage}'.");
            }

            // A workflow-variable capture writes the canonical root variable frame (#972), not a durable
            // value row — the frame is the one runtime truth for workflow-scope variables.
            if (targetsVariable)
            {
                workflowVariableWrites[TargetVariableKey(capture, node)] = encoding;
                continue;
            }

            var durableValueId = $"durable-{capture.ValueId}";
            var metadata = capture.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            metadata[RuntimeMetadataKeys.StorageDriverKey] = capture.StorageDriverKey;
            AddReservedTargetMetadata(capture, metadata);
            var state = new DurableValueState(
                durableValueId,
                workflowExecutionId,
                capture.ValueId,
                capture.Type,
                capture.Lifecycle,
                encoding.Storage,
                encoding.InlineValue,
                encoding.ExternalReference,
                activityExecutionId,
                capturedAt,
                RuntimeModelMetadata.Snapshot(metadata));
            changes.Add(new RuntimeStateChange<DurableValueState>(
                durableValueId,
                RuntimeStateChangeOperation.Upsert,
                state,
                state.Metadata));
        }

        return rowFault is not null
            ? new RuntimeOutputCaptureProjection([], new Dictionary<string, RuntimeDurableValueEncoding>(StringComparer.Ordinal)) { VariableWrites = [rowFault], LostUpdateFault = rowFault }
            : new RuntimeOutputCaptureProjection(changes, workflowVariableWrites) { VariableWrites = rowWrites };
    }

    /// <summary>
    /// Spec 1221 T003 (ADR 0048 D-9: frames and rows follow one set of rules): a capture into a closed scope's variable
    /// row is judged by the commit rule against the version the row carries, through the same read frame a body node
    /// reads it from, and the row stores the resulting version, digest and writer. Without an invocation or a durable
    /// value store the row is written blind and unversioned, as before.
    /// </summary>
    private async ValueTask<(RuntimeStateChange<DurableValueState> Row, VariableWriteRecord? Record)> VersionRowAsync(
        string workflowExecutionId,
        ExecutableNode node,
        RuntimeStateChange<DurableValueState> row,
        ActivityExecutionState boundary,
        ActivityExecutionState? invocation,
        VariableWriteSource source,
        IDurableValueStateStore? durableValueStateStore,
        ValueProtectionPolicy capturedPolicy,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        // The store is the caller's scoped one, so the row is read in the drain's persistence scope.
        if (invocation is null || durableValueStateStore is null || row.State is not { } written)
            return (row, null);

        // Row metadata does not carry the value's protection, so the committed value and the one it overwrites are given
        // the captured value's policy: a record keeps them as protected as the capture was.
        var hidesValue = capturedPolicy.HidesValue();
        var key = written.Metadata[RuntimeMetadataKeys.BoundaryReferenceKey];
        var current = await durableValueStateStore.FindAsync(workflowExecutionId, written.DurableValueId, cancellationToken);
        var value = WithPolicy(RuntimeInputBindingStateProjection.ProjectBoundaryEnvelopes([written], boundary.InvocationId, ActivityExecutionScopeValues.VariableRole)[key], capturedPolicy);
        var frame = ClosedScopeVariableRows.ReadFrame(boundary, current is null ? [] : [current]);
        // A variable the scope never stored reads as null at version 0, as an undeclared frame value would.
        var previous = frame.Values.TryGetValue(key, out var stored) ? WithPolicy(stored, capturedPolicy) : ValueEnvelope.Null(value.Type, capturedPolicy);
        frame = new VariableFrameState(frame.FrameId, frame.ScopeId, frame.ActivationId, frame.ParentFrameId, frame.Kind,
            new Dictionary<string, ValueEnvelope>(frame.Values, StringComparer.Ordinal) { [key] = previous },
            frame.Revision, frame.Status, frame.Versions);
        var writer = new VariableWriter(source, node.ExecutableNodeId, invocation.InvocationId, invocation.Attempts?.LastOrDefault()?.AttemptId);
        var write = frame.Commit(key, value, frame.Revision, invocation.InputSnapshot?.InputReadSet?.BaseFor(frame.ScopeId, key), LostUpdateModes.Of(node), writer);
        // A row has no frame revision: its commit order is the version the write produced. Whether the value is hidden
        // comes from the captured value's policy; the row's own metadata does not carry it.
        var record = VariableWriteRecord.From(frame, write, key, writer, capturedAt);
        record = new VariableWriteRecord(record.ScopeId, record.VariableKey, record.Writer, record.Outcome, record.BaseVersion,
            record.CurrentVersion, record.NewVersion, record.NewVersion, record.OccurredAt, record.OverwrittenWriter, record.OverwrittenValue)
        {
            CommitSequence = record.CommitSequence,
            FrameId = record.FrameId,
            Digest = hidesValue ? null : record.Digest
        };
        if (write.Decision.Outcome == VariableCommitOutcome.LostUpdateFault)
            return (row, record with { RejectedValue = value });

        var versioned = ClosedScopeVariableRows.WithVersion(written, write.Frame.VersionOf(key), hidesValue);
        return (row with { State = versioned, Metadata = versioned.Metadata }, VariableWriteRecord.IsRecorded(write.Decision) ? record : null);
    }

    private static ValueEnvelope WithPolicy(ValueEnvelope envelope, ValueProtectionPolicy policy) => envelope.Presence switch
    {
        ValuePresence.ExplicitNull => ValueEnvelope.Null(envelope.Type, policy),
        ValuePresence.Present when envelope.InlineValue is { } inline => ValueEnvelope.Inline(envelope.Type, inline, policy),
        ValuePresence.Present when envelope.ExternalReference is { } external => ValueEnvelope.External(envelope.Type, external, policy),
        _ => envelope
    };

    /// <summary>
    /// A capture into a graph variable writes the variable row of the producer's nearest closed activity
    /// execution scope, the row that boundary's activation owns, so later body nodes and its output mappings read it.
    /// A graph variable has no withheld form, so a withheld value is refused as for a durable output.
    /// </summary>
    private async ValueTask<RuntimeStateChange<DurableValueState>> ProjectGraphVariableAsync(
        string workflowExecutionId,
        string activityExecutionId,
        RuntimeOutputCapture capture,
        ActivityResultProjectionContract projectionContract,
        ValueEnvelope projected,
        IActivityCompletionTransition transition,
        string targetScopeId,
        ActivityExecutionState? closedScopeBoundary,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        if (projected.Presence == ValuePresence.Withheld || projected.HoldsValueRequiringEncryption())
            throw SecretBindingDiagnostics.WithheldOutputNotCaptured(capture.OutputName);
        ValidateDurableCaptureBoundary(capture, projected);
        if (closedScopeBoundary is null ||
            !StringComparer.Ordinal.Equals(closedScopeBoundary.Execution.ExecutableNodeId, targetScopeId))
        {
            throw new InvalidOperationException(
                $"Output capture '{capture.OutputName}' writes a variable of graph scope '{targetScopeId}', which is not the nearest closed activity execution scope of activity '{activityExecutionId}'.");
        }

        var value = capture.ConversionPlan is null
            ? StringComparer.Ordinal.Equals(projectionContract.Path, "$")
                ? transition.Result
                : projected.Presence == ValuePresence.ExplicitNull
                    ? null
                    : projected.InlineValue
            : ToEncodedValue(_valueConversionExecutor.Convert(projected, capture.ConversionPlan));
        var encoding = await _storageDrivers.GetRequired(capture.StorageDriverKey).EncodeAsync(value, capture.Type, cancellationToken);
        var boundaryId = closedScopeBoundary.InvocationId;
        var referenceKey = capture.Metadata[RuntimeMetadataKeys.TargetVariableReferenceKey];
        var durableValueId = ActivityExecutionScopeKeys.Variable(boundaryId, referenceKey);
        var metadata = ActivityExecutionScopeValues.Metadata(
            boundaryId,
            ActivityExecutionScopeValues.VariableRole,
            referenceKey,
            capture.StorageDriverKey,
            variableName: capture.Metadata.GetValueOrDefault(RuntimeMetadataKeys.VariableName, referenceKey));
        var state = new DurableValueState(
            durableValueId,
            workflowExecutionId,
            durableValueId,
            capture.Type,
            DurableValueLifecycle.Instance,
            encoding.Storage,
            encoding.InlineValue,
            encoding.ExternalReference,
            boundaryId,
            capturedAt,
            RuntimeModelMetadata.Snapshot(metadata));
        return new RuntimeStateChange<DurableValueState>(durableValueId, RuntimeStateChangeOperation.Upsert, state, state.Metadata);
    }

    private static string TargetVariableKey(RuntimeOutputCapture capture, ExecutableNode node) =>
        capture.Metadata.TryGetValue(RuntimeMetadataKeys.TargetVariableReferenceKey, out var variableKey) &&
        !string.IsNullOrWhiteSpace(variableKey)
            ? variableKey
            : throw new InvalidOperationException(
                $"Output capture '{capture.OutputName}' on executable node '{node.ExecutableNodeId}' targets a workflow variable but carries no '{RuntimeMetadataKeys.TargetVariableReferenceKey}' metadata.");

    private static void ValidateDurableCaptureBoundary(RuntimeOutputCapture capture, ValueEnvelope projected)
    {
        if (projected.Presence != ValuePresence.Present)
            return;

        var sourceRepresentation = capture.ConversionPlan?.SourceRepresentation ??
                                   ValueRepresentationDefaults.Infer(projected.Type);
        if (sourceRepresentation != ValueRepresentation.TransientResource && projected.TransientResource is null)
            return;

        throw new InvalidOperationException(
            $"VF-ACT-005: Output capture '{capture.OutputName}' cannot persist source representation '{ValueRepresentation.TransientResource}' " +
            $"into destination storage policy '{capture.Storage}' using driver '{capture.StorageDriverKey}'. " +
            "Use an execution-local activity-result binding for live resources, or model an explicit DurableReference/resource-handle output before persisting.");
    }

    private static void AddReservedTargetMetadata(
        RuntimeOutputCapture capture,
        IDictionary<string, string> metadata)
    {
        if (capture.ValueId.StartsWith(RuntimeWorkflowStateSeed.OutputValueIdPrefix, StringComparison.Ordinal))
        {
            metadata.TryAdd(
                RuntimeMetadataKeys.OutputName,
                capture.OutputName);
        }
    }

    private static object? ToEncodedValue(ValueEnvelope envelope) => envelope.Presence switch
    {
        ValuePresence.ExplicitNull => null,
        ValuePresence.Present when envelope.InlineValue.HasValue => envelope.InlineValue.Value,
        ValuePresence.Present => throw new InvalidOperationException("Output capture conversion produced an external reference, which cannot be encoded by an output storage driver."),
        _ => throw new InvalidOperationException("Output capture conversion produced an absent value after completion projection.")
    };
}

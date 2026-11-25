using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Core.Services;

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
    private readonly IWellKnownTypeRegistry? _types;
    private readonly IRuntimeTypeNullability? _typeDescriptors;
    private WorkflowValuePolicies _policies = WorkflowValuePolicies.Off;
    private IExecutionWarningSink? _warnings;

    public RuntimeOutputCaptureProjector(
        IRuntimeDurableValueStorageDriverRegistry storageDrivers,
        IRuntimeValueConversionExecutor valueConversionExecutor,
        IWellKnownTypeRegistry types,
        IRuntimeTypeNullability typeDescriptors) : this(storageDrivers, valueConversionExecutor)
    {
        _types = types;
        _typeDescriptors = typeDescriptors;
    }

    // Projectors are registered as singletons. Policy and warning state belongs to this invocation's clone.
    internal RuntimeOutputCaptureProjector WithValuePolicies(WorkflowValuePolicies policies, IExecutionWarningSink? warnings) =>
        new(_storageDrivers, _valueConversionExecutor, _types!, _typeDescriptors!)
        {
            _policies = policies,
            _warnings = warnings
        };

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

    public ValueTask<RuntimeOutputCaptureProjection> ProjectAsync(
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
        IDurableValueStateStore? durableValueStateStore = null) =>
        ProjectAsync(workflowExecutionId, activityExecutionId, node, transition, completion, capturedAt, cancellationToken,
            closedScopeBoundary, invocation, source, durableValueStateStore, rowCommit: null);

    /// <param name="rowCommit">
    /// The step's row commit, when its session already wrote closed-scope rows in this commit (spec 1241): the captures are
    /// judged against the rows as those writes left them. Null builds a fresh one over <paramref name="durableValueStateStore"/>.
    /// </param>
    internal async ValueTask<RuntimeOutputCaptureProjection> ProjectAsync(
        string workflowExecutionId,
        string activityExecutionId,
        ExecutableNode node,
        IActivityCompletionTransition transition,
        ActivityCompletionProjection completion,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken,
        ActivityExecutionState? closedScopeBoundary,
        ActivityExecutionState? invocation,
        VariableWriteSource source,
        IDurableValueStateStore? durableValueStateStore,
        ClosedScopeRowCommit? rowCommit)
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
        // Spec 1241 FR-007: every row write of this commit is judged against the row as the previous one left it, and
        // a capture never conflicts with the invocation's own earlier write.
        rowCommit ??= durableValueStateStore is null ? null : new ClosedScopeRowCommit(durableValueStateStore, _storageDrivers);
        foreach (var capture in node.OutputCaptures.Values
                     .Where(item => item.CaptureOnSuccessfulCompletion)
                     .OrderBy(item => item.OutputName, StringComparer.Ordinal))
        {
            if (!contract.Result.Projections.TryGetValue(capture.OutputName, out var projectionContract))
                throw new InvalidOperationException($"Output capture '{capture.OutputName}' is not declared by activity contract '{contract.ActivityTypeKey}'.");
            if (!completion.Projections.TryGetValue(capture.OutputName, out var projected))
                throw new InvalidOperationException($"Activity completion did not produce declared output projection '{capture.OutputName}'.");
            if (projected.Presence == ValuePresence.Absent)
            {
                if (!node.Metadata.ContainsKey(RuntimeMetadataKeys.ScriptResultInputKey) || capture.OutputName != "result")
                    continue;
                if (!_policies.UndefinedAsNull)
                    throw new UndefinedCapturedResultException(node.AuthoredActivityId, capture.OutputName);
                projected = ValueEnvelope.Null(projected.Type, projected.Policy);
            }
            var encodeEnvelope = capture.ConversionPlan is not null || projected.Presence == ValuePresence.ExplicitNull;
            projected = NormalizeCaptureValue(capture, projected);
            if (capture.Metadata.TryGetValue(RuntimeMetadataKeys.CaptureTargetScopeId, out var targetScopeId))
            {
                var row = await ProjectGraphVariableAsync(
                    workflowExecutionId, activityExecutionId, capture, projectionContract, projected, transition, encodeEnvelope, targetScopeId,
                    closedScopeBoundary, capturedAt, rowCommit, cancellationToken);
                var versioned = await VersionRowAsync(workflowExecutionId, node, row, closedScopeBoundary!, invocation, source, rowCommit,
                    projected.Policy, capturedAt, cancellationToken);
                if (versioned.Record is { } record)
                {
                    rowWrites.Add(record);
                    invocation = InvocationVariableBases.Advance(invocation!, [record]);
                }
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

            var value = CaptureValue(capture, projectionContract, projected, transition, encodeEnvelope);
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
    /// row is judged by the commit rule through <see cref="ClosedScopeRowCommit"/>, against the row as this commit last
    /// wrote it. Without an invocation or a row commit the row is written blind and unversioned, as before.
    /// </summary>
    private static async ValueTask<(RuntimeStateChange<DurableValueState> Row, VariableWriteRecord? Record)> VersionRowAsync(
        string workflowExecutionId,
        ExecutableNode node,
        RuntimeStateChange<DurableValueState> row,
        ActivityExecutionState boundary,
        ActivityExecutionState? invocation,
        VariableWriteSource source,
        ClosedScopeRowCommit? rows,
        ValueProtectionPolicy capturedPolicy,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        if (invocation is null || rows is null || row.State is not { } written)
            return (row, null);

        var key = written.Metadata[RuntimeMetadataKeys.BoundaryReferenceKey];
        var writer = new VariableWriter(source, node.ExecutableNodeId, invocation.InvocationId, invocation.Attempts?.LastOrDefault()?.AttemptId);
        return await rows.CommitAsync(workflowExecutionId, node, written, boundary,
            InvocationVariableBases.For(invocation, boundary.Execution.ExecutableNodeId, key), writer, capturedPolicy, capturedAt, cancellationToken);
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
        bool encodeEnvelope,
        string targetScopeId,
        ActivityExecutionState? closedScopeBoundary,
        DateTimeOffset capturedAt,
        ClosedScopeRowCommit? rowCommit,
        CancellationToken cancellationToken)
    {
        // A driver may store externally while encoding. Enforce the existing destination before calling it.
        if (closedScopeBoundary is not null && rowCommit is not null)
        {
            var id = ActivityExecutionScopeKeys.Variable(closedScopeBoundary.InvocationId, capture.Metadata[RuntimeMetadataKeys.TargetVariableReferenceKey]);
            var previous = await rowCommit.FindAsync(workflowExecutionId, id, cancellationToken);
            if (previous is not null)
                projected = WithPolicy(projected, ValuePolicyCombiner.Combine(projected.Policy, DurableValuePolicies.Read(previous), $"Graph variable '{capture.OutputName}'"));
        }
        if (projected.Presence == ValuePresence.Withheld || projected.HoldsValueRequiringEncryption())
            throw SecretBindingDiagnostics.WithheldOutputNotCaptured(capture.OutputName);
        ValidateDurableCaptureBoundary(capture, projected);
        if (closedScopeBoundary is null ||
            !StringComparer.Ordinal.Equals(closedScopeBoundary.Execution.ExecutableNodeId, targetScopeId))
        {
            throw new InvalidOperationException(
                $"Output capture '{capture.OutputName}' writes a variable of graph scope '{targetScopeId}', which is not the nearest closed activity execution scope of activity '{activityExecutionId}'.");
        }

        var value = CaptureValue(capture, projectionContract, projected, transition, encodeEnvelope);
        var encoding = await _storageDrivers.GetRequired(capture.StorageDriverKey).EncodeAsync(value, capture.Type, cancellationToken);
        var boundaryId = closedScopeBoundary.InvocationId;
        var referenceKey = capture.Metadata[RuntimeMetadataKeys.TargetVariableReferenceKey];
        var durableValueId = ActivityExecutionScopeKeys.Variable(boundaryId, referenceKey);
        var metadata = new Dictionary<string, string>(ActivityExecutionScopeValues.Metadata(
            boundaryId,
            ActivityExecutionScopeValues.VariableRole,
            referenceKey,
            capture.StorageDriverKey,
            variableName: capture.Metadata.GetValueOrDefault(RuntimeMetadataKeys.VariableName, referenceKey)), StringComparer.Ordinal);
        if (projected.Policy.IsSensitive) metadata[RuntimeMetadataKeys.IsSensitive] = bool.TrueString;
        if (projected.Policy.RequiresEncryption) metadata[RuntimeMetadataKeys.RequiresEncryption] = bool.TrueString;
        if (projected.Policy.RedactionMode is not null) metadata[RuntimeMetadataKeys.RedactionMode] = projected.Policy.RedactionMode;
        if (projected.Policy.RetentionPolicy is not null) metadata[RuntimeMetadataKeys.RetentionPolicy] = projected.Policy.RetentionPolicy;
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
        state = DurableValuePolicies.ApplyMinimum(state, projected.Policy, $"Graph variable '{referenceKey}'");
        return new RuntimeStateChange<DurableValueState>(durableValueId, RuntimeStateChangeOperation.Upsert, state, state.Metadata);
    }

    private ValueEnvelope NormalizeCaptureValue(RuntimeOutputCapture capture, ValueEnvelope projected)
    {
        if (projected.Presence == ValuePresence.Withheld || projected.HoldsValueRequiringEncryption())
            return projected;
        ValidateDurableCaptureBoundary(capture, projected);
        var converted = capture.ConversionPlan is null ? projected : _valueConversionExecutor.Convert(projected, capture.ConversionPlan);
        if (converted.Presence != ValuePresence.ExplicitNull)
            return converted;
        var targetType = capture.ConversionPlan?.TargetType.ToTypeReference()
            ?? capture.Type.Schema?.Deserialize<TypeReference>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new TypeReference(capture.Type.Kind);
        return NullPolicyDefaults.NormalizeNull(converted.Retype(ValueTypeDescriptor.FromTypeReference(targetType)),
            NullPolicyDefaults.AcceptsNull(targetType, _types, _typeDescriptors), _policies,
            $"Output capture '{capture.OutputName}'", _types, _warnings);
    }

    private static object? CaptureValue(RuntimeOutputCapture capture, ActivityResultProjectionContract projection,
        ValueEnvelope projected, IActivityCompletionTransition transition, bool encodeEnvelope)
    {
        // Preserve the existing CLR driver boundary for whole results; serialization remains S-9 owned.
        return encodeEnvelope ? ToEncodedValue(projected)
            : StringComparer.Ordinal.Equals(projection.Path, "$") ? transition.Result : projected.InlineValue;
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

    private sealed class UndefinedCapturedResultException(string activity, string output) : InvalidOperationException($"VF-UNDEF-001: Activity '{activity}' bound script result '{output}' produced no value; return a value, remove the result binding, or enable UndefinedAsNull."), Elsa.Expressions.Core.Contracts.IValueFreeExpressionFailure
    {
        public string ValueFreeMessage => "VF-UNDEF-001: The bound script result produced no value; return a value, remove the result binding, or enable UndefinedAsNull.";
    }

    private static object? ToEncodedValue(ValueEnvelope envelope) => envelope.Presence switch
    {
        ValuePresence.ExplicitNull => null,
        ValuePresence.Present when envelope.InlineValue.HasValue => envelope.InlineValue.Value,
        ValuePresence.Present => throw new InvalidOperationException("Output capture conversion produced an external reference, which cannot be encoded by an output storage driver."),
        _ => throw new InvalidOperationException("Output capture conversion produced an absent value after completion projection.")
    };
}

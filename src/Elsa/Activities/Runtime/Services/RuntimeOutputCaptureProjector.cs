using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Projects selected atomic activity-result values into their published durable targets.
/// The returned changes are committed in the same checkpoint as activity completion.
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

    public async ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> ProjectAsync(
        string workflowExecutionId,
        string activityExecutionId,
        ExecutableNode node,
        IActivityCompletionTransition transition,
        ActivityCompletionProjection completion,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(completion);

        if (node.OutputCaptures.Count == 0)
            return [];
        var contract = node.ActivityContract
            ?? throw new InvalidOperationException($"Executable node '{node.ExecutableNodeId}' has output captures but no pinned activity contract.");
        var changes = new List<RuntimeStateChange<DurableValueState>>();
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

            var value = capture.ConversionPlan is null or { Operation: ValueConversionOperation.Identity }
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

        return changes;
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
        else if (capture.ValueId.StartsWith(RuntimeWorkflowStateSeed.VariableValueIdPrefix, StringComparison.Ordinal))
        {
            metadata.TryAdd(
                RuntimeMetadataKeys.VariableName,
                capture.ValueId[RuntimeWorkflowStateSeed.VariableValueIdPrefix.Length..]);
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

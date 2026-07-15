using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

internal static class ActivityOutputPublisher
{
    public static IDictionary<string, OutputArgument> BuildOutputArguments(ExecutableNode executableNode) =>
        executableNode.OutputCaptures.ToDictionary(
            item => item.Key,
            item => (OutputArgument)new OutputArgument<object?>(new RuntimeOutputMemoryBlockReference(item.Key)),
            StringComparer.Ordinal);

    public static void PublishActivityOutputs(
        IRuntimeActivityOutputRegister activityOutputRegister,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset recordedAt)
    {
        foreach (var output in outputs)
        {
            executableNode.OutputCaptures.TryGetValue(output.OutputName, out var capture);
            var metadata = new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
                [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
            };

            activityOutputRegister.Set(new ActiveActivityOutput(
                key: new ActiveActivityOutputKey(workItem.WorkflowExecutionId, invokePayload.ActivityExecutionId, output.OutputName),
                value: SerializeOutputValue(output.Value),
                type: capture?.Type,
                recordedAt: recordedAt,
                metadata: metadata));
        }
    }

    public static ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> BuildDurableOutputChangesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset capturedAt,
        IRuntimeDurableValueStorageDriverRegistry storageDrivers,
        CancellationToken cancellationToken = default) =>
        BuildDurableOutputChangesAsync(workItem, invokePayload.ActivityExecutionId, invokePayload.ExecutableNodeId, executableNode, outputs, capturedAt, storageDrivers, cancellationToken);

    /// <summary>
    /// Builds the durable-value upserts for an activity's <c>CaptureOnSuccessfulCompletion</c> outputs, keyed on the
    /// activity-execution and node ids so both the invoke path (<see cref="RuntimeInvokeActivityCommandPayload"/>)
    /// and the resume path (<see cref="Elsa.Workflows.Runtime.Core.Models.RuntimeResumeBookmarkCommandPayload"/>)
    /// persist outputs identically — a resume target that sets outputs (e.g. the mid-flow <c>HttpEndpoint</c>,
    /// spec 089 D) durably captures them exactly as a synchronous completion does.
    /// </summary>
    public static async ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> BuildDurableOutputChangesAsync(
        RuntimeSchedulerWorkItem workItem,
        string activityExecutionId,
        string executableNodeId,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset capturedAt,
        IRuntimeDurableValueStorageDriverRegistry storageDrivers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageDrivers);
        var outputByName = outputs.ToDictionary(output => output.OutputName, StringComparer.Ordinal);
        var captures = executableNode.OutputCaptures.Values
            .Where(capture => capture.CaptureOnSuccessfulCompletion)
            .Where(capture => outputByName.ContainsKey(capture.OutputName))
            .ToArray();
        var drivers = captures.ToDictionary(
            capture => capture.OutputName,
            capture => storageDrivers.GetRequired(capture.StorageDriverKey),
            StringComparer.Ordinal);
        var changes = new List<RuntimeStateChange<DurableValueState>>(captures.Length);
        foreach (var capture in captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = outputByName[capture.OutputName];
            var encoding = await drivers[capture.OutputName].EncodeAsync(output.Value, capture.Type, cancellationToken);
            changes.Add(NewDurableValueChange(workItem, activityExecutionId, executableNodeId, capture, encoding, capturedAt));
        }
        return changes;
    }

    private static RuntimeStateChange<DurableValueState> NewDurableValueChange(
        RuntimeSchedulerWorkItem workItem,
        string activityExecutionId,
        string executableNodeId,
        RuntimeOutputCapture capture,
        RuntimeDurableValueEncoding encoding,
        DateTimeOffset capturedAt)
    {
        var metadata = capture.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CommandId] = workItem.CommandId;
        metadata[RuntimeMetadataKeys.OutputName] = capture.OutputName;
        metadata[RuntimeMetadataKeys.ActivityExecutionId] = activityExecutionId;
        metadata[RuntimeMetadataKeys.ExecutableNodeId] = executableNodeId;
        metadata[RuntimeMetadataKeys.StorageDriverKey] = capture.StorageDriverKey;
        var durableValueId = $"durable-{capture.ValueId}";
        var encodedType = capture.Type with { Id = capture.StorageDriverKey };
        var state = new DurableValueState(
            durableValueId: durableValueId,
            workflowExecutionId: workItem.WorkflowExecutionId,
            valueId: capture.ValueId,
            type: encodedType,
            lifecycle: capture.Lifecycle,
            storage: encoding.Storage,
            inlineValue: encoding.InlineValue,
            externalReference: encoding.ExternalReference,
            sourceActivityExecutionId: activityExecutionId,
            capturedAt: capturedAt,
            metadata: metadata);

        return new RuntimeStateChange<DurableValueState>(
            StateId: durableValueId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: state,
            Metadata: metadata);
    }

    public static JsonElement SerializeOutputValue(object? value) =>
        value is JsonElement json
            ? json.Clone()
            : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));

    public static JsonElement? SerializeCapturedValue(RuntimePayloadCaptureDecision decision, object? value, string? valueName = null, RuntimeValueTypeDescriptor? type = null) =>
        decision.Mode switch
        {
            RuntimePayloadCaptureMode.Payload => SerializeOutputValue(value),
            RuntimePayloadCaptureMode.DiagnosticSnapshot => DefaultDiagnosticSnapshotFactory.Capture(value, valueName, type),
            _ => null
        };

    public static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildInputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
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
                    activityExecutionId: invokePayload.ActivityExecutionId,
                    valueName: input.Name,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
                        [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
                    }));
                return ActivityExecutionInspectionValueSnapshot.FromDecision(
                    input.Name,
                    ActivityExecutionInspectionValueSubject.ActivityInput,
                    decision,
                    type,
                    capturedAt,
                    SerializeCapturedValue(decision, input.Value, input.Name, type),
                    isSensitive: false,
                    metadata: decision.Metadata);
            })
            .ToArray();

    public static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildOutputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset capturedAt) =>
        BuildOutputValueSnapshots(
            payloadCapturePolicy,
            workItem,
            invokePayload.ActivityExecutionId,
            invokePayload.ExecutableNodeId,
            executableNode,
            outputs,
            capturedAt);

    public static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildOutputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        string activityExecutionId,
        string executableNodeId,
        ExecutableNode executableNode,
        IReadOnlyCollection<RecordedActivityOutput> outputs,
        DateTimeOffset capturedAt) =>
        outputs
            .Select(output =>
            {
                executableNode.OutputCaptures.TryGetValue(output.OutputName, out var capture);
                var type = capture?.Type ?? TypeDescriptorFor(output.Value);
                var decision = payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
                    RuntimePayloadCaptureSubject.ActivityOutput,
                    workItem.WorkflowExecutionId,
                    capturedAt,
                    activityExecutionId: activityExecutionId,
                    valueName: output.OutputName,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = executableNodeId,
                        [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
                    }));
                return ActivityExecutionInspectionValueSnapshot.FromDecision(
                    output.OutputName,
                    ActivityExecutionInspectionValueSubject.ActivityOutput,
                    decision,
                    type,
                    capturedAt,
                    SerializeCapturedValue(decision, output.Value, output.OutputName, type),
                    isSensitive: false,
                    metadata: decision.Metadata);
            })
            .ToArray();

    public static RuntimeValueTypeDescriptor RuntimeObjectType { get; } = new("clr", typeof(object).FullName, null);

    public static RuntimeValueTypeDescriptor TypeDescriptorFor(object? value) =>
        value is null ? RuntimeObjectType : new RuntimeValueTypeDescriptor("clr", value.GetType().FullName, null);

    public static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.InvokeReason] = invokePayload.Reason,
            [RuntimeMetadataKeys.InvokeSchedulerWorkItemId] = workItem.WorkItemId
        };

        return new ActivityFaultIncidentRecordRequest(
            CheckpointCommitter: checkpointCommitter,
            WorkItem: workItem,
            ActivityExecutionId: invokePayload.ActivityExecutionId,
            ExecutableNodeId: invokePayload.ExecutableNodeId,
            State: state,
            Exception: exception,
            SubStatus: subStatus,
            ActivityMetadata: activityMetadata,
            IncidentMetadata: new Dictionary<string, string>(),
            ValueSnapshots: valueSnapshots);
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
}

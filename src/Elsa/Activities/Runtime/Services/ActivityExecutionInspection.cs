using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

internal static class ActivityExecutionInspection
{
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
        ActivityContract contract,
        ActivityInputSnapshot snapshot,
        DateTimeOffset capturedAt) =>
        BuildInputValueSnapshots(
            payloadCapturePolicy,
            workItem,
            invokePayload.ActivityExecutionId,
            invokePayload.ExecutableNodeId,
            contract,
            snapshot,
            RuntimeMetadataKeys.InvokeSchedulerWorkItemId,
            capturedAt);

    public static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildInputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        string activityExecutionId,
        string executableNodeId,
        ActivityContract contract,
        ActivityInputSnapshot snapshot,
        string schedulerWorkItemMetadataKey,
        DateTimeOffset capturedAt) =>
        snapshot.Values
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item =>
            {
                var input = contract.Inputs[item.Key];
                var value = item.Value;
                var type = new RuntimeValueTypeDescriptor("alias", value.Type.Alias, value.Type.Schema);
                var decision = payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
                    RuntimePayloadCaptureSubject.ActivityInput,
                    workItem.WorkflowExecutionId,
                    capturedAt,
                    activityExecutionId: activityExecutionId,
                    valueName: input.Name,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = executableNodeId,
                        [schedulerWorkItemMetadataKey] = workItem.WorkItemId
                    }));
                var payload = value.Presence switch
                {
                    ValuePresence.Present when value.InlineValue.HasValue => SerializeCapturedValue(decision, value.InlineValue.Value, input.Name, type),
                    ValuePresence.ExplicitNull => SerializeCapturedValue(decision, null, input.Name, type),
                    _ => null
                };
                return ActivityExecutionInspectionValueSnapshot.FromDecision(
                    input.Name,
                    ActivityExecutionInspectionValueSubject.ActivityInput,
                    decision,
                    type,
                    capturedAt,
                    payload,
                    value.Policy.IsSensitive,
                    decision.Metadata);
            })
            .ToArray();

    public static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> BuildOutputValueSnapshots(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload invokePayload,
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
                    activityExecutionId: invokePayload.ActivityExecutionId,
                    valueName: output.OutputName,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = invokePayload.ExecutableNodeId,
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

}

using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Captures a completing container's final container-scoped variable values as inspection evidence
/// (ADR 0027, #210). Each value is routed through the configured <see cref="IRuntimePayloadCapturePolicy"/>
/// so that completed-scope evidence is retained in inspection/history only as the retention/redaction
/// policy allows — the snapshot is always recorded, but its payload is captured only when the policy
/// permits. Shared by the activity-completion handlers so both the empty-container path and the
/// child-completion path emit identical evidence.
/// </summary>
public static class RuntimeContainerVariableEvidence
{
    public static IReadOnlyList<ActivityExecutionInspectionValueSnapshot> Capture(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        RuntimeContainerScopeService scopeService,
        ExecutableNode containerNode,
        ActivityExecutionState containerState,
        string workflowExecutionId,
        string activityExecutionId,
        string workItemId,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(payloadCapturePolicy);
        ArgumentNullException.ThrowIfNull(scopeService);
        ArgumentNullException.ThrowIfNull(containerNode);
        ArgumentNullException.ThrowIfNull(containerState);

        return scopeService.ReadScopeVariableValues(containerNode, containerState)
            .Select(variable =>
            {
                var type = TypeDescriptorFor(variable.Value);
                var decision = payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
                    RuntimePayloadCaptureSubject.ContainerVariable,
                    workflowExecutionId,
                    capturedAt,
                    activityExecutionId: activityExecutionId,
                    valueName: variable.Name,
                    type: type,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.ExecutableNodeId] = containerNode.ExecutableNodeId,
                        [RuntimeMetadataKeys.SchedulerWorkItemId] = workItemId,
                        ["referenceKey"] = variable.ReferenceKey
                    }));
                return ActivityExecutionInspectionValueSnapshot.FromDecision(
                    variable.Name,
                    ActivityExecutionInspectionValueSubject.ContainerVariable,
                    decision,
                    type,
                    capturedAt,
                    decision.CapturesPayload ? SerializeValue(variable.Value) : null,
                    isSensitive: false,
                    metadata: decision.Metadata);
            })
            .ToArray();
    }

    private static JsonElement SerializeValue(object? value) =>
        value is JsonElement json
            ? json.Clone()
            : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));

    private static RuntimeValueTypeDescriptor TypeDescriptorFor(object? value) =>
        new("clr", value?.GetType().FullName ?? typeof(object).FullName, null);
}

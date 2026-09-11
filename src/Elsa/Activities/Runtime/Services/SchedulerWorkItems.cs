using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Work-item factories and payload plumbing shared by the activity scheduler work handlers (invoke, resume,
/// parent completion, parent notification). Every handler derives the same follow-up work items from its own
/// source work item; only the source payload's identity fields and, for the parent-completion handler, the
/// command metadata the derived items inherit differ, so those are parameters here.
/// </summary>
internal static class SchedulerWorkItems
{
    /// <summary>
    /// Deserializes a handler's payload with the shared message shape and validation filter. A malformed payload
    /// surfaces as <see cref="InvalidOperationException"/> wrapping the JSON, unsupported-type, or payload
    /// constructor argument error; other exceptions propagate untouched.
    /// </summary>
    /// <param name="commandLabel">The command label leading every message, e.g. <c>InvokeActivity</c>.</param>
    /// <param name="payloadDescription">The payload noun in the messages, e.g. <c>invoke activity payload</c>.</param>
    /// <param name="validationParamNames">The payload constructor parameter names whose argument errors count as validation failures.</param>
    /// <param name="deserialize">Overrides the default fresh parse of the work item's payload element.</param>
    public static T DeserializePayload<T>(
        RuntimeSchedulerWorkItem workItem,
        string commandLabel,
        string payloadDescription,
        string[] validationParamNames,
        Func<RuntimeSchedulerWorkItem, JsonElement, T?>? deserialize = null) where T : class =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: $"{commandLabel} scheduler work item requires a {payloadDescription}.",
            resolvedToNullMessage: $"{commandLabel} scheduler work item payload resolved to null.",
            invalidPayloadMessage: $"{commandLabel} scheduler work item payload is not a valid {payloadDescription}.",
            deserialize: deserialize ?? (static (_, payload) => payload.Deserialize<T>()),
            isPayloadValidationException: exception => IsPayloadValidationException(exception, validationParamNames));

    /// <summary>
    /// True when <paramref name="exception"/> is a payload validation failure: a JSON or unsupported-type error, or
    /// an argument error raised by one of the payload constructor parameters in <paramref name="validationParamNames"/>.
    /// </summary>
    public static bool IsPayloadValidationException(Exception exception, string[] validationParamNames) =>
        exception is JsonException or NotSupportedException ||
        exception is ArgumentException { ParamName: { } paramName } && validationParamNames.Contains(paramName);

    /// <summary>
    /// Builds the <see cref="WorkflowExecutionCommandKind.CompleteActivity"/> work item that reports the completed
    /// activity invocation upward. <paramref name="commandMetadata"/> defaults to the source work item's own; the
    /// parent-completion handler passes a copy stripped of fault-evaluation keys instead.
    /// </summary>
    public static RuntimeSchedulerWorkItem NewCompletionWorkItem(
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem sourceWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        ActivityExecutionState completedState,
        string? skippedSubStatus = null,
        IReadOnlyDictionary<string, string>? commandMetadata = null)
    {
        var now = timeProvider.GetUtcNow();
        var payload = new RuntimeCompleteActivityCommandPayload(
            pinnedExecutable,
            executableNodeId,
            activityExecutionId,
            completedState.ParentActivityExecutionId,
            completedState.BranchId,
            SchedulerWorkHandlerHelpers.ReadCompletionOutcomeNames(completedState, skippedSubStatus),
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(sourceWorkItem.WorkItemId, $"complete:{activityExecutionId}"),
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(sourceWorkItem.CommandId, $"complete:{activityExecutionId}"),
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: sourceWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(sourceWorkItem.IdempotencyKey, $"complete:{activityExecutionId}"),
            enqueuedAt: now,
            recordedAt: now,
            sequence: sourceWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata ?? sourceWorkItem.CommandMetadata,
            envelopeMetadata: sourceWorkItem.EnvelopeMetadata);
    }

    /// <summary>
    /// Builds one <see cref="WorkflowExecutionCommandKind.ScheduleActivity"/> work item per child schedule request
    /// staged by the activity identified by <paramref name="parentActivityExecutionId"/>. Each child inherits
    /// <paramref name="commandMetadata"/> (the source work item's own by default) overlaid with the request's
    /// metadata and the parent/child identity keys. Lazily evaluated: ids and timestamps are minted as enumerated.
    /// </summary>
    public static IEnumerable<RuntimeSchedulerWorkItem> NewChildActivityScheduleWorkItems(
        TimeProvider timeProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        RuntimeSchedulerWorkItem sourceWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        string parentActivityExecutionId,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> scheduleRequests,
        IReadOnlyDictionary<string, string>? commandMetadata = null)
    {
        var inheritedMetadata = commandMetadata ?? sourceWorkItem.CommandMetadata;
        var requests = scheduleRequests.ToArray();
        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            var now = timeProvider.GetUtcNow();
            var childActivityExecutionId = idGenerator.NewActivityExecutionId();
            var payload = new RuntimeScheduleActivityCommandPayload(
                pinnedExecutable,
                request.ExecutableNodeId,
                childActivityExecutionId,
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                request.SchedulingActivityExecutionId ?? parentActivityExecutionId,
                parentActivityExecutionId,
                request.SchedulingProvenance == ActivitySchedulingProvenance.Empty
                    ? ActivitySchedulingProvenance.From(
                        sourceWorkItem.WorkflowExecutionId,
                        parentActivityExecutionId,
                        request.SchedulingActivityExecutionId ?? parentActivityExecutionId,
                        branchId: null,
                        iterationId: null,
                        executionPathId: null,
                        executionScopeId: null,
                        schedulingCause: RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                        metadata: request.Metadata)
                    : request.SchedulingProvenance,
                request.IterationFrame);

            var childCommandMetadata = inheritedMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            foreach (var item in request.Metadata)
                childCommandMetadata[item.Key] = item.Value;

            childCommandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId] = parentActivityExecutionId;
            childCommandMetadata[RuntimeMetadataKeys.ChildExecutableNodeId] = request.ExecutableNodeId;

            yield return new RuntimeSchedulerWorkItem(
                workItemId: RuntimeChainId.Derive(sourceWorkItem.WorkItemId, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
                commandId: RuntimeChainId.Derive(sourceWorkItem.CommandId, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                envelopeId: sourceWorkItem.EnvelopeId,
                idempotencyKey: RuntimeChainId.Derive(sourceWorkItem.IdempotencyKey, $"schedule-child:{request.ExecutableNodeId}:{childActivityExecutionId}"),
                enqueuedAt: now,
                recordedAt: now,
                sequence: sourceWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
                payload: JsonSerializer.SerializeToElement(payload),
                commandMetadata: childCommandMetadata,
                envelopeMetadata: sourceWorkItem.EnvelopeMetadata);
        }
    }
}

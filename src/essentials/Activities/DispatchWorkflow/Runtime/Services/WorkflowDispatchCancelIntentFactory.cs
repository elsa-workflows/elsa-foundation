using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

internal static class WorkflowDispatchCancelIntentFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static RuntimePostCommitIntent Create(WorkflowDispatchRecord record, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        var payload = new WorkflowDispatchChildCancelPayload(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId);
        return new RuntimePostCommitIntent(
            intentId: identity.ChildCancelIntentId,
            workflowExecutionId: record.ParentWorkflowExecutionId,
            kind: DispatchWorkflowConstants.CancelChildIntentKind,
            recordedAt: recordedAt,
            activityExecutionId: record.ParentActivityExecutionId,
            idempotencyKey: identity.ChildCancelIdempotencyKey,
            payload: JsonSerializer.SerializeToElement(payload, SerializerOptions),
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = record.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = record.ChildWorkflowExecutionId
            });
    }
}

using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Pure deterministic first-checkpoint/root continuation construction, shared by dispatch and physical witnesses.</summary>
internal static class NativeIngressStartLineage
{
    internal static RuntimeSchedulerWorkItem Build(RuntimeSchedulerWorkItem start, WorkflowExecutionStartCommandPayload payload,
        string rootNodeId)
    {
        if (payload.StartAuthority?.StandaloneTrigger is null && payload.StimulusDeliveryOrigin is null) throw new InvalidOperationException("A sealed ingress start origin is required.");
        var now = start.EnqueuedAt;
        var metadata = start.CommandMetadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        // The saved-start marker protects only initial admission, never later activity continuations.
        metadata.Remove(SavedStimulusStartSeal.OriginMetadata);
        var envelopeMetadata = start.EnvelopeMetadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        envelopeMetadata.Remove(SavedStimulusStartSeal.OriginMetadata);
        metadata[RuntimeMetadataKeys.WorkflowStartedAt] = now.ToString("O", CultureInfo.InvariantCulture);
        var activityId = $"{start.WorkflowExecutionId}:activity:root";
        var attempt = new ActivityExecutionAttemptLineage(1, activityId, null);
        var provenance = ActivitySchedulingProvenance.From(start.WorkflowExecutionId, null, null, null, null, null, null,
            RuntimeScheduleActivityCommandPayload.WorkflowStartReason, attempt: attempt);
        var schedule = new RuntimeScheduleActivityCommandPayload(payload.PinnedExecutable, rootNodeId, activityId,
            RuntimeScheduleActivityCommandPayload.WorkflowStartReason, schedulingProvenance: provenance);
        var root = new RuntimeSchedulerWorkItem(RuntimeChainId.Derive(start.WorkItemId, $"schedule:{rootNodeId}"),
            start.WorkflowExecutionId, RuntimeChainId.Derive(start.CommandId, $"schedule:{rootNodeId}"),
            WorkflowExecutionCommandKind.ScheduleActivity, start.EnvelopeId, RuntimeChainId.Derive(start.IdempotencyKey, $"schedule:{rootNodeId}"),
            now, start.RecordedAt, start.Sequence is { } sequence ? sequence + 2 : null, JsonSerializer.SerializeToElement(schedule),
            metadata, envelopeMetadata, attempt: attempt);
        var intent = new RuntimePostCommitIntent($"{start.WorkItemId}:postcommit:schedule:root:{rootNodeId}", start.WorkflowExecutionId,
            RuntimePostCommitIntentKinds.EnqueueSchedulerWork, now, null, root.IdempotencyKey, JsonSerializer.SerializeToElement(root),
            new Dictionary<string, string> { ["runtime.sourceSchedulerWorkItemId"] = start.WorkItemId,
                ["runtime.schedulerWorkItemId"] = root.WorkItemId, ["runtime.rootExecutableNodeId"] = rootNodeId });
        var checkpoint = new RuntimeCheckpointCommandPayload(payload.PinnedExecutable, RuntimeCheckpointNames.WorkflowStarted,
            [], RuntimeCheckpointCommandPayload.WorkflowStartReason, [intent], payload.Variables, payload.Inputs, payload.StimulusInput,
            payload.TriggerNodeId, payload.RunKind, payload.PinnedSource, payload.ParentWorkflowExecutionId, payload.CorrelationId,
            payload.TenantId, payload.Partition, payload.Authority, payload.DispatchNestingDepth, payload.TestScope, payload.TriggerMetadata)
        {
            IngressOrigin = payload.StartAuthority?.StandaloneTrigger,
            StimulusDeliveryOrigin = payload.StimulusDeliveryOrigin,
            HistoryCapturePolicy = payload.HistoryCapturePolicy,
            HistoryCaptureEnabledAtAdmission = payload.HistoryCaptureEnabledAtAdmission
        };
        var checkpointMetadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        { [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory };
        if (payload.StimulusDeliveryOrigin is { } saved)
        {
            checkpointMetadata[SavedStimulusStartSeal.OriginMetadata] = JsonSerializer.Serialize(saved);
            envelopeMetadata = new(envelopeMetadata, StringComparer.Ordinal) { [SavedStimulusStartSeal.OriginMetadata] = JsonSerializer.Serialize(saved) };
        }
        return new(RuntimeChainId.Derive(start.WorkItemId, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}"), start.WorkflowExecutionId,
            RuntimeChainId.Derive(start.CommandId, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}"), WorkflowExecutionCommandKind.Checkpoint,
            start.EnvelopeId, RuntimeChainId.Derive(start.IdempotencyKey, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}"),
            now, start.RecordedAt, start.Sequence is { } checkpointSequence ? checkpointSequence + 1 : null,
            JsonSerializer.SerializeToElement(checkpoint), checkpointMetadata, envelopeMetadata);
    }
}

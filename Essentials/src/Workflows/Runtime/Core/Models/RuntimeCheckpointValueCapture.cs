using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable evidence contribution from one actual checkpoint owner, including a known empty contribution.</summary>
public sealed record RuntimeCheckpointValueCapture
{
    [JsonConstructor]
    public RuntimeCheckpointValueCapture(string checkpointId, string? schedulerWorkItemId, IReadOnlyList<string> evidenceIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId); ArgumentNullException.ThrowIfNull(evidenceIds);
        if (checkpointId.Length > 1024 || schedulerWorkItemId is not null && (string.IsNullOrWhiteSpace(schedulerWorkItemId) || schedulerWorkItemId.Length > 1024))
            throw new ArgumentException("Checkpoint evidence requires bounded exact source identities.");
        var snapshot = evidenceIds.ToArray();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        // Bound each validation window without imposing a new width limit on the native checkpoint.
        foreach (var window in snapshot.Chunk(256))
        foreach (var id in window)
            if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || !identities.Add(id))
                throw new ArgumentException("Checkpoint evidence requires bounded exact source identities.");
        CheckpointId = checkpointId; SchedulerWorkItemId = schedulerWorkItemId; EvidenceIds = Array.AsReadOnly(snapshot);
    }
    public string CheckpointId { get; }
    public string? SchedulerWorkItemId { get; }
    public IReadOnlyList<string> EvidenceIds { get; }
}

public enum ActivityValueCapturePhase { Unknown, Invoke, Resume, Structural, Output, Container }

/// <summary>Safe actual-owner provenance, without copying scheduler payloads or metadata.</summary>
public sealed record ActivityValueCaptureSource
{
    public ActivityValueCaptureSource(string workflowExecutionId, string? schedulerWorkItemId, ActivityValueCapturePhase phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (workflowExecutionId.Length > 1024 || schedulerWorkItemId is not null && (string.IsNullOrWhiteSpace(schedulerWorkItemId) || schedulerWorkItemId.Length > 1024) || !Enum.IsDefined(phase))
            throw new ArgumentException("Value capture requires bounded actual source provenance.");
        WorkflowExecutionId = workflowExecutionId; SchedulerWorkItemId = schedulerWorkItemId; Phase = phase;
    }
    public string WorkflowExecutionId { get; }
    public string? SchedulerWorkItemId { get; }
    public ActivityValueCapturePhase Phase { get; }
    public static ActivityValueCaptureSource FromWorkItem(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return new(workItem.WorkflowExecutionId, workItem.WorkItemId, workItem.CommandKind switch
        {
            WorkflowExecutionCommandKind.InvokeActivity => ActivityValueCapturePhase.Invoke,
            WorkflowExecutionCommandKind.ResumeBookmark => ActivityValueCapturePhase.Resume,
            WorkflowExecutionCommandKind.NotifyParentActivity or WorkflowExecutionCommandKind.DeliverInterventionDecision or
                WorkflowExecutionCommandKind.DeliverChildControl or WorkflowExecutionCommandKind.CompleteActivity => ActivityValueCapturePhase.Structural,
            _ => ActivityValueCapturePhase.Unknown
        });
    }
}

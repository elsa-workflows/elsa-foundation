using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>Metadata-only description of one captured child workflow input.</summary>
public sealed record WorkflowDispatchInputCaptureView(
    string Name,
    string ValueType,
    string CaptureMode,
    bool ValueCaptured);

/// <summary>
/// Allowlist-only operational view of detached workflow dispatch. It intentionally omits tenant,
/// partition, authority, correlation, arbitrary metadata, input/output values, and exception material.
/// </summary>
public sealed record WorkflowDispatchView(
    string DispatchId,
    string ParentWorkflowExecutionId,
    string ParentActivityExecutionId,
    string ChildWorkflowExecutionId,
    WorkflowDispatchMode Mode,
    WorkflowDispatchStatus Status,
    string ChildArtifactId,
    string ChildDefinitionId,
    string ChildDefinitionVersionId,
    string ChildVersion,
    string ChildSourceType,
    WorkflowRunKind RunKind,
    IReadOnlyCollection<WorkflowDispatchInputCaptureView> InputCaptures,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? DiagnosticCode,
    string? DiagnosticCategory)
{
    public string? DeliveryIncidentId { get; init; }
    public string? DeliveryDeadLetterId { get; init; }
    public int DeliveryGeneration { get; init; }
    public int DeliveryAttemptCount { get; init; }
    public DateTimeOffset? DeliveryFirstAttemptAt { get; init; }
    public DateTimeOffset? DeliveryFailedAt { get; init; }
    public bool RedriveEligible { get; init; }

    public static WorkflowDispatchView From(WorkflowDispatchRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.Mode,
            record.Status,
            record.ChildExecutable.ArtifactId,
            record.ChildExecutable.DefinitionId,
            record.ChildExecutable.DefinitionVersionId,
            record.ChildExecutable.ArtifactVersion,
            record.ChildSource.SourceKind,
            record.RunKind,
            record.InputDescriptors
                .Select(descriptor => new WorkflowDispatchInputCaptureView(
                    descriptor.Name,
                    descriptor.ValueType,
                    "metadataOnly",
                    ValueCaptured: false))
                .ToArray(),
            record.CreatedAt,
            record.UpdatedAt,
            WorkflowDispatchLifecycle.ReadSafeDiagnosticCode(record),
            WorkflowDispatchLifecycle.ReadSafeDiagnosticCategory(record))
        {
            DeliveryIncidentId = WorkflowDispatchLifecycle.ReadDeliveryIncidentId(record),
            DeliveryDeadLetterId = WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(record),
            DeliveryGeneration = WorkflowDispatchLifecycle.ReadDeliveryGeneration(record),
            DeliveryAttemptCount = WorkflowDispatchLifecycle.ReadDeliveryAttemptCount(record),
            DeliveryFirstAttemptAt = WorkflowDispatchLifecycle.ReadDeliveryFirstAttemptAt(record),
            DeliveryFailedAt = WorkflowDispatchLifecycle.ReadDeliveryFailedAt(record),
            RedriveEligible = WorkflowDispatchLifecycle.IsRedriveEligible(record)
        };
    }
}

/// <summary>Allowlist-only result of a separately authorized dispatch redrive request.</summary>
public sealed record WorkflowDispatchRedriveView(
    string DispatchId,
    WorkflowDispatchRedriveDisposition Disposition,
    WorkflowDispatchStatus? Status,
    int DeliveryGeneration,
    string? DeliveryIncidentId,
    string? DeliveryDeadLetterId)
{
    public static WorkflowDispatchRedriveView From(WorkflowDispatchRedriveResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.DispatchId,
            result.Disposition,
            result.Status,
            result.Generation,
            result.IncidentId,
            result.DeadLetterId);
    }
}

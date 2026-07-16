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
            WorkflowDispatchLifecycle.ReadSafeDiagnosticCategory(record));
    }
}

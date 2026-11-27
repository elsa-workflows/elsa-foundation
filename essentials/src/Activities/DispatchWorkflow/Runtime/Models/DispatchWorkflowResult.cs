using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>
/// The stored terminal result of a waited child: its status, the allow-listed fault and cancellation diagnostics and
/// the child-control transport. It is not an activity output and carries no child outputs (spec 1245 FR-004); the
/// child's output values travel as <see cref="WorkflowDispatchParentResumePayload.Output"/>.
/// </summary>
public sealed class DispatchWorkflowResult
{
    public DispatchWorkflowResult(
        string childWorkflowExecutionId,
        WorkflowDispatchStatus status,
        IReadOnlyDictionary<string, string>? diagnosticMetadata = null)
        : this(childWorkflowExecutionId, status, diagnosticMetadata, control: null)
    {
    }

    [JsonConstructor]
    public DispatchWorkflowResult(
        string childWorkflowExecutionId,
        WorkflowDispatchStatus status,
        IReadOnlyDictionary<string, string>? diagnosticMetadata,
        RuntimeChildControlTransport? control)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childWorkflowExecutionId);

        if (status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started)
            throw new ArgumentOutOfRangeException(nameof(status), status, "A DispatchWorkflow result requires a terminal child status.");
        // Root authored control can commit before a later workflow cancellation wins. Keep both facts;
        // start-delivery failure has no admitted root and therefore cannot carry root control.
        if (control is not null && (!control.Batch.IsTerminal ||
            !StringComparer.Ordinal.Equals(control.Batch.ChildWorkflowExecutionId, childWorkflowExecutionId.Trim()) ||
            status == WorkflowDispatchStatus.DispatchFailed))
            throw new ArgumentException("DispatchWorkflow control must match the exact terminal child result.", nameof(control));

        var diagnosticSnapshot = (diagnosticMetadata ?? new Dictionary<string, string>())
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        switch (status)
        {
            case WorkflowDispatchStatus.Faulted:
                DispatchWorkflowDiagnostics.ValidateFaulted(diagnosticSnapshot);
                break;
            case WorkflowDispatchStatus.Cancelled:
                DispatchWorkflowDiagnostics.ValidateCancelled(diagnosticSnapshot);
                break;
            case WorkflowDispatchStatus.DispatchFailed:
                DispatchWorkflowDiagnostics.ValidateDispatchFailed(diagnosticSnapshot);
                break;
        }

        ChildWorkflowExecutionId = childWorkflowExecutionId.Trim();
        Status = status;
        DiagnosticMetadata = new ReadOnlyDictionary<string, string>(diagnosticSnapshot);
        Control = control;
    }

    public string ChildWorkflowExecutionId { get; }
    public WorkflowDispatchStatus Status { get; }
    public IReadOnlyDictionary<string, string> DiagnosticMetadata { get; }

    /// <summary>Authored control carried separately from fixed safe diagnostics; absent on legacy and ordinary results.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeChildControlTransport? Control { get; }

    internal static bool SupportsParentResume(WorkflowDispatchStatus status) =>
        status is WorkflowDispatchStatus.Completed or WorkflowDispatchStatus.Faulted or WorkflowDispatchStatus.Cancelled or WorkflowDispatchStatus.DispatchFailed;
}

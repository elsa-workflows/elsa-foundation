using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>A JSON-safe child output captured by waited DispatchWorkflow execution.</summary>
public sealed class DispatchWorkflowOutput
{
    public DispatchWorkflowOutput(string name, JsonElement? value, string? declaredType, bool isRedacted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (value is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("A captured child output value must be valid JSON.", nameof(value));
        if (isRedacted && value.HasValue)
            throw new ArgumentException("A redacted child output cannot carry a value.", nameof(value));

        Name = name.Trim();
        Value = value?.Clone();
        DeclaredType = string.IsNullOrWhiteSpace(declaredType) ? null : declaredType.Trim();
        IsRedacted = isRedacted;
    }

    public string Name { get; }
    public JsonElement? Value { get; }
    public string? DeclaredType { get; }
    public bool IsRedacted { get; }
}

/// <summary>
/// Reserved structured result for waited execution. #676 leaves it unset; #679 populates it for a terminal child.
/// </summary>
public sealed class DispatchWorkflowResult
{
    public DispatchWorkflowResult(
        string childWorkflowExecutionId,
        WorkflowDispatchStatus status,
        IReadOnlyCollection<DispatchWorkflowOutput>? outputs = null,
        IReadOnlyDictionary<string, string>? diagnosticMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childWorkflowExecutionId);

        if (status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started)
            throw new ArgumentOutOfRangeException(nameof(status), status, "A DispatchWorkflow result requires a terminal child status.");

        var outputSnapshot = (outputs ?? []).ToArray();
        if (status != WorkflowDispatchStatus.Completed && outputSnapshot.Length != 0)
            throw new ArgumentException("Only a completed child may expose captured outputs.", nameof(outputs));

        if (outputSnapshot.Any(output => output is null))
            throw new ArgumentException("A captured child output cannot be null.", nameof(outputs));
        if (outputSnapshot.Select(output => output.Name).Distinct(StringComparer.Ordinal).Count() != outputSnapshot.Length)
            throw new ArgumentException("Captured child output names must be unique.", nameof(outputs));

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
        Outputs = Array.AsReadOnly(outputSnapshot);
        DiagnosticMetadata = new ReadOnlyDictionary<string, string>(diagnosticSnapshot);
    }

    public string ChildWorkflowExecutionId { get; }
    public WorkflowDispatchStatus Status { get; }
    public IReadOnlyCollection<DispatchWorkflowOutput> Outputs { get; }
    public IReadOnlyDictionary<string, string> DiagnosticMetadata { get; }

    internal static bool SupportsParentResume(WorkflowDispatchStatus status) =>
        status is WorkflowDispatchStatus.Completed or WorkflowDispatchStatus.Faulted or WorkflowDispatchStatus.Cancelled or WorkflowDispatchStatus.DispatchFailed;
}

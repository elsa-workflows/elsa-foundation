namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Server-prepared direct model admission; never bound from an external request body.</summary>
public sealed record AuthoredModelAdmissionRequest(
    WorkflowActivationSlot Slot,
    WorkflowExecutableSourceReference Source,
    RuntimeExecutionMaterial Material,
    string WorkflowExecutionId,
    RuntimeModelExecutionCommand Command,
    DateTimeOffset RecordedAt);

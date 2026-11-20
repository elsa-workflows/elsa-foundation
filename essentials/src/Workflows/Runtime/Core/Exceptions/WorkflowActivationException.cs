namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Raised when activation cannot be attempted or an infrastructure fault escapes its lifecycle.</summary>
public sealed class WorkflowActivationException(
    string workflowDefinitionId,
    string slotName,
    string activationId,
    string message,
    Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string WorkflowDefinitionId { get; } = workflowDefinitionId;
    public string SlotName { get; } = slotName;
    public string ActivationId { get; } = activationId;
}

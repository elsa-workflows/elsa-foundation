namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Raised when an activation cannot be attempted or an infrastructure fault escapes the activation lifecycle.
/// </summary>
/// <remarks>
/// §2.23.5: no raw storage, serialization or IO exception leaves the activation boundary. Whatever the lease
/// manager or a store threw is preserved as <see cref="Exception.InnerException"/>, and the identifiers needed to
/// locate the failure are carried as first-class properties rather than being formatted into the message only.
/// Refusals and compensated failures are NOT exceptions — they are returned as
/// <see cref="Models.WorkflowActivationResult"/> values.
/// </remarks>
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

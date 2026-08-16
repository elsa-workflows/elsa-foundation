namespace Elsa.Persistence.Groundwork.Exceptions;

/// <summary>
/// Raised when the durable activation ledger cannot complete a read or a CAS transition because of an
/// infrastructure fault.
/// </summary>
/// <remarks>
/// §2.23.5: no raw storage, serialization or IO exception leaves the authority boundary. The original fault is
/// preserved as <see cref="Exception.InnerException"/> and the slot identifiers needed to locate it are carried
/// as first-class properties. Refusals — a stale revision or a foreign owner — are NOT exceptions: they are
/// returned as an unsuccessful <c>WorkflowActivationTransition</c>, exactly as the in-memory authority does.
/// </remarks>
public sealed class GroundworkWorkflowActivationAuthorityException(
    string workflowDefinitionId,
    string slotName,
    string message,
    Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string WorkflowDefinitionId { get; } = workflowDefinitionId;
    public string SlotName { get; } = slotName;
}

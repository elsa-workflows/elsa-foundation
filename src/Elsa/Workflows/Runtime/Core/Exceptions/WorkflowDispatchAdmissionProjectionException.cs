namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Signals that a child was admitted but its Started lifecycle projection could not yet be persisted. Outbox
/// processors must leave the claim recoverable instead of classifying this as a child-start delivery failure.
/// </summary>
public sealed class WorkflowDispatchAdmissionProjectionException(string dispatchId, Exception innerException)
    : Exception($"Workflow dispatch '{dispatchId}' child admission requires lifecycle projection repair.", innerException)
{
    public string DispatchId { get; } = dispatchId;
}

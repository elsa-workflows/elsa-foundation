using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Thrown when a live drain can no longer renew or cleanly release its execution ownership.</summary>
public sealed class RuntimeExecutionOwnershipLostException : Exception
{
    public RuntimeExecutionOwnershipLostException(
        RuntimeExecutionLease lease,
        string operation,
        RuntimeExecutionOwnershipTransitionStatus? transitionStatus,
        Exception? innerException = null)
        : base(CreateMessage(lease, operation, transitionStatus), innerException)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        WorkflowExecutionId = lease.WorkflowExecutionId;
        LeaseId = lease.LeaseId;
        FencingToken = lease.FencingToken;
        Operation = operation;
        TransitionStatus = transitionStatus;
    }

    public string WorkflowExecutionId { get; }
    public string LeaseId { get; }
    public long FencingToken { get; }
    public string Operation { get; }
    public RuntimeExecutionOwnershipTransitionStatus? TransitionStatus { get; }

    private static string CreateMessage(
        RuntimeExecutionLease lease,
        string operation,
        RuntimeExecutionOwnershipTransitionStatus? transitionStatus)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var outcome = transitionStatus?.ToString() ?? "Failed";
        return $"Workflow execution '{lease.WorkflowExecutionId}' lost ownership lease '{lease.LeaseId}' " +
               $"while attempting to {operation}; the ownership transition outcome was '{outcome}'.";
    }
}

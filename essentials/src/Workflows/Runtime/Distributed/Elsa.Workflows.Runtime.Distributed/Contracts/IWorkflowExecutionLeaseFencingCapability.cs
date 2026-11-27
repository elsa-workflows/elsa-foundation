namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Reports whether the selected active persistence path has admitted checkpoint fencing.
/// Implementations belong to persistence leaves; the distributed runtime consumes only this
/// provider-neutral claim.
/// </summary>
public interface IWorkflowExecutionLeaseFencingCapability
{
    bool IsAvailable { get; }
}

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Thrown when a checkpoint commit is presented with a fencing token that is not the current owner's token for the
/// workflow execution — i.e. a stale (or otherwise non-owning) writer attempted to persist state. The single-writer
/// ownership contract (RT-2) fences such writers out at the checkpoint-commit funnel so a superseded drainer cannot
/// overwrite the state owned by the current lease holder.
/// </summary>
public sealed class RuntimeStaleFencingTokenException : Exception
{
    public RuntimeStaleFencingTokenException(string workflowExecutionId, long presentedFencingToken, long currentFencingToken)
        : base(
            $"Checkpoint commit for workflow execution '{workflowExecutionId}' presented fencing token {presentedFencingToken} " +
            $"but the current ownership fencing token is {currentFencingToken}. A stale or non-owning writer is fenced out of the " +
            "single-writer commit path (RT-2).")
    {
        WorkflowExecutionId = workflowExecutionId;
        PresentedFencingToken = presentedFencingToken;
        CurrentFencingToken = currentFencingToken;
    }

    public string WorkflowExecutionId { get; }
    public long PresentedFencingToken { get; }
    public long CurrentFencingToken { get; }
}

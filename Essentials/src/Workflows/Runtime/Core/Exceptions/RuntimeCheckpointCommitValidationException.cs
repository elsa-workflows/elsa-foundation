namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A checkpoint commit broke a checkpoint rule: <c>RuntimeCheckpointCommitValidator</c> refused its shape, or a shared rule
/// function a store applies found it conflicts with persisted state in a way no later attempt can change. Delivering the
/// same work again produces the same commit and the same refusal, so a post-commit handler reports it as a permanent failure.
/// </summary>
/// <remarks>
/// A concurrency conflict is not a rule violation and never uses this type: a stale fencing token, a lost scheduler-work
/// claim, or a lost alteration claim keeps its own exception, because a retry by the current owner can succeed. Derives
/// from <see cref="InvalidOperationException"/> so existing callers that catch that type are unaffected.
/// </remarks>
public sealed class RuntimeCheckpointCommitValidationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    /// <summary>
    /// Whether <paramref name="exception"/> is a checkpoint rule violation or carries one as an inner exception. Runtime
    /// layers between a commit and a post-commit handler wrap what they catch: a drain aggregates the failures of its
    /// observers, so the violation a child's terminal commit raised reaches the child-start handler inside an
    /// <see cref="AggregateException"/>.
    /// </summary>
    public static bool IsCauseOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var pending = new Stack<Exception>([exception]);
        while (pending.TryPop(out var current))
        {
            if (current is RuntimeCheckpointCommitValidationException)
                return true;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    pending.Push(inner);
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }

        return false;
    }
}

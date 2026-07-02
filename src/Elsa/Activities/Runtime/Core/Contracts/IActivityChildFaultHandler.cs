using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Implemented by composite activities that own child-<b>fault</b> continuation semantics. Where
/// <see cref="IActivityChildCompletionHandler"/> is invoked when a child reaches <c>Completed</c>, this is
/// invoked when a child branch reaches a terminal <c>Faulted</c> state, letting a fork/join composite react
/// deterministically (e.g. fault the join when it can no longer be satisfied) rather than waiting forever for
/// a completion that will never arrive.
/// </summary>
/// <remarks>
/// A composite that does not implement this interface is unaffected: a faulted child remains a blocking
/// incident and is not propagated to the parent, preserving the existing "a faulted step halts the flow"
/// behavior for sequential containers.
/// </remarks>
public interface IActivityChildFaultHandler
{
    /// <summary>
    /// Handles a terminal fault of one child activity execution.
    /// </summary>
    ValueTask OnChildFaultedAsync(ActivityChildFaultedContext context);
}

using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Implemented by composite activities that own child-completion continuation semantics.
/// </summary>
public interface IActivityChildCompletionHandler
{
    /// <summary>
    /// Handles completion of one child activity execution.
    /// </summary>
    ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context);
}

using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Abstractions;

/// <summary>
/// Base class for custom activities.
/// </summary>
public abstract class ActivityBase : IActivity
{
    /// <summary>
    /// Override this method to implement activity-specific logic.
    /// </summary>
    protected virtual ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        Execute(context);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Produces the immutable transition returned to the engine for ordinary leaf activities.
    /// </summary>
    protected virtual async ValueTask<ActivityTransition> ExecuteTransitionAsync(IActivityExecutionContext context)
    {
        await ExecuteAsync(context);
        return ActivityTransition.Complete(
            ActivityUnit.Value,
            "Done");
    }

    /// <summary>
    /// Override this method to implement activity-specific logic.
    /// </summary>
    protected virtual void Execute(IActivityExecutionContext context)
    {
    }

    async ValueTask<ActivityTransition> IActivity.ExecuteAsync(IActivityExecutionContext context)
    {
        return await ExecuteTransitionAsync(context);
    }
}

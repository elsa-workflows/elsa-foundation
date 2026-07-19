using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Represents an activity, which is an atomic unit of operation within a workflow.
/// </summary>
public interface IActivity
{
    /// <summary>
    /// Invoked when the activity executes.
    /// </summary>
    ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context);
}

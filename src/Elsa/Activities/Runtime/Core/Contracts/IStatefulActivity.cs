using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Engine-facing protocol for resuming a stateful activity without reflecting over its generic
/// state, trigger, or result types.
/// </summary>
public interface IStatefulActivity
{
    /// <summary>
    /// Validates and materializes the frozen state and trigger documents before invoking the typed
    /// author-facing resume method.
    /// </summary>
    ValueTask<ActivityTransition> ResumeAsync(ActivityResumeRequest request);
}

/// <summary>
/// Author-facing protocol for an activity whose durable suspension state and trigger delivery are
/// strongly typed. The runtime activates a new CLR object before every call.
/// </summary>
public interface IStatefulActivity<TResult, TState, TTrigger> : IStatefulActivity
{
    /// <summary>Runs the initial attempt on a fresh activity activation.</summary>
    ValueTask<ActivityTransition<TResult, TState>> ExecuteAsync(ActivityExecutionContext context);

    /// <summary>
    /// Runs a resume attempt on a fresh activity activation with the last committed state and one
    /// already validated trigger delivery.
    /// </summary>
    ValueTask<ActivityTransition<TResult, TState>> ResumeAsync(ActivityResumeContext<TState, TTrigger> context);
}

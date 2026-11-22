using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Evaluates administrative pause holds at named runtime boundaries without changing workflow continuation state.
/// </summary>
public interface IRuntimePauseDecisionProvider
{
    /// <summary>
    /// Returns whether scheduler work can advance through the requested pause boundary.
    /// </summary>
    ValueTask<SchedulerPauseDecision> DecideAsync(RuntimePauseDecisionRequest request, CancellationToken cancellationToken = default);
}

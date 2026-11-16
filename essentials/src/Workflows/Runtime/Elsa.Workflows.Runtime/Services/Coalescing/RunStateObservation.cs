using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// What a drain read of its run's workflow execution state when it began, under the run's ownership lease: the state,
/// or <see langword="null"/> when the store held none. It exists so that "read and found nothing" is not confused with
/// "not read". One session may start from it: a later session of the same drain begins after the drain may have
/// committed, when what was read no longer describes the store.
/// </summary>
internal sealed class RunStateObservation(WorkflowExecutionState? state)
{
    private int _taken;

    public WorkflowExecutionState? State { get; } = state;

    /// <summary>Claims the observation for one session. Only the first call succeeds.</summary>
    public bool TryTake() => Interlocked.Exchange(ref _taken, 1) == 0;
}

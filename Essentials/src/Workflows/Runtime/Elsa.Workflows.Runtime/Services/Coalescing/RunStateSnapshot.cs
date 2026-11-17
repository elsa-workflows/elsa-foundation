using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// The workflow execution state a drain read when it began, kept so the drain does not read the same row again
/// before it has a state of its own. It may be "no state": a run that was never committed.
/// </summary>
/// <remarks>
/// The session answers a read of its run's state from what it has buffered or committed, and before that from this
/// snapshot. The snapshot is an observation made under the run's single-writer lease, and it stops answering as soon
/// as a write of that state is attempted, whether or not the write then lands. It answers nothing until
/// <see cref="Begin"/>, and nothing again after <see cref="End"/>. <see cref="End"/> can arrive from another thread
/// (a cancellation callback, or a flow that captured the session), so all state is read and written under one lock.
/// </remarks>
internal sealed class RunStateSnapshot
{
    private readonly object _gate = new();
    private bool _begun;
    private bool _ended = true;
    private WorkflowExecutionState? _state;

    /// <summary>
    /// Lets the snapshot answer with what the drain read. Only the first call counts, and not after
    /// <see cref="End"/>: a snapshot that was ended never answers again.
    /// </summary>
    public void Begin(WorkflowExecutionState? observed)
    {
        lock (_gate)
        {
            if (_begun)
                return;

            _begun = true;
            _ended = false;
            _state = observed;
        }
    }

    /// <summary>
    /// Whether the snapshot still stands. When it does, <paramref name="state"/> is what the drain read, which is
    /// <see langword="null"/> for a run that had no state.
    /// </summary>
    public bool TryGet(out WorkflowExecutionState? state)
    {
        lock (_gate)
        {
            state = _ended ? null : _state;
            return !_ended;
        }
    }

    /// <summary>
    /// Stops answering for good. Used when the state may have been written, and when writes of the run may reach the
    /// store by a path this session does not see.
    /// </summary>
    public void End()
    {
        lock (_gate)
        {
            _begun = true;
            _ended = true;
            _state = null;
        }
    }
}

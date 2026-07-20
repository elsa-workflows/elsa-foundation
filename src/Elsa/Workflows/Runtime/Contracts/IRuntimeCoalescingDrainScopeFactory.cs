using Elsa.Workflows.Runtime.Core.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Establishes a checkpoint-coalescing session for the duration of a single workflow-execution drain (E3-6, RT-10).
/// Registered only when coalescing is opted in; when absent the drain coordinator runs its existing path unchanged, so
/// the default (Immediate) behavior is byte-for-byte preserved.
/// </summary>
public interface IRuntimeCoalescingDrainScopeFactory
{
    /// <summary>
    /// Begins a coalescing session scope for the supplied workflow execution. When <paramref name="maxSegmentCheckpoints"/>
    /// is supplied it overrides the host-configured segment cap for this run (ADR 0032 R5 per-workflow authored cadence);
    /// when null the host default applies.
    /// </summary>
    IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, int? maxSegmentCheckpoints = null);
}

/// <summary>
/// A coalescing session scope. While alive, the ambient coalescing session redirects the runtime store decorators onto
/// an in-memory working set. At quiescence the buffered segment is folded and flushed through the checkpoint committer
/// (so W5 ownership fencing gates it); disposing pops the ambient scope.
/// </summary>
public interface IRuntimeCoalescingDrainScope : IAsyncDisposable
{
    /// <summary>The coalescing session owned by this scope.</summary>
    RuntimeCoalescingSession Session { get; }

    /// <summary>
    /// Folds the buffered segment and flushes it atomically through the checkpoint committer, then advances the durable
    /// scheduler queue. A no-op if the segment already flushed at a boundary. Not called when the drain threw, so a
    /// crash mid-segment discards the buffer and replays from the last flushed state.
    /// </summary>
    ValueTask FlushAtQuiescenceAsync(CancellationToken cancellationToken = default);
}

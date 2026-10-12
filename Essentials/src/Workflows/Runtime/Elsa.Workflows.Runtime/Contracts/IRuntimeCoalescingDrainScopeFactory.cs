using Elsa.Workflows.Runtime.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Contracts;

/// <summary>
/// Establishes a checkpoint-folding session for the duration of a single workflow-execution drain. Registered by
/// <c>AddPersistenceModes</c>; when absent the drain coordinator runs every execution Durable.
/// </summary>
public interface IRuntimeCoalescingDrainScopeFactory
{
    /// <summary>Begins a folding session scope for the supplied workflow execution, using the host's settings.</summary>
    IRuntimeCoalescingDrainScope Begin(string workflowExecutionId);
}

/// <summary>
/// A coalescing session scope. While alive, the ambient coalescing session redirects the runtime store decorators onto
/// an in-memory working set. At quiescence the buffered segment is folded and flushed through the checkpoint committer
/// (so single-writer ownership fencing gates it); disposing pops the ambient scope.
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

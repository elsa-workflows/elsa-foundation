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
/// an in-memory working set. Empty sessions reconcile queue consumption, and buffered sessions terminally fold their
/// exact Prepared membership through the durable provider. Disposing pops the ambient scope.
/// </summary>
public interface IRuntimeCoalescingDrainScope : IAsyncDisposable
{
    /// <summary>The coalescing session owned by this scope.</summary>
    RuntimeCoalescingSession Session { get; }

    /// <summary>
    /// Reconciles an empty session or atomically terminal-folds its buffered Prepared state. A no-op if the session was
    /// already deactivated. Not called when the drain threw.
    /// </summary>
    ValueTask FlushAtQuiescenceAsync(CancellationToken cancellationToken = default);
}

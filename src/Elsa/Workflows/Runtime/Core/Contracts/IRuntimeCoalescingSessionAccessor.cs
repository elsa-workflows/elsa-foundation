using Elsa.Workflows.Runtime.Core.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Ambient, explicitly-scoped accessor for the active checkpoint-coalescing session (E3-6, RT-10). Mirrors the
/// push/pop shape of <see cref="IRuntimeExecutionOwnershipContextAccessor"/>: the drain coordinator establishes a
/// coalescing session for the duration of a drain, and the coalescing store/queue/outbox/state-store decorators
/// resolve it to redirect reads and writes onto the in-memory working set instead of the durable stores. When no
/// session is active (the default, Immediate policy), every decorator is a byte-for-byte pass-through to its inner
/// durable store.
/// </summary>
public interface IRuntimeCoalescingSessionAccessor
{
    /// <summary>The coalescing session owning the current drain scope, or <see langword="null"/> when none is active.</summary>
    RuntimeCoalescingSession? Current { get; }

    /// <summary>Pushes a coalescing session scope; dispose the returned handle to pop it.</summary>
    IDisposable Push(RuntimeCoalescingSession? session);
}

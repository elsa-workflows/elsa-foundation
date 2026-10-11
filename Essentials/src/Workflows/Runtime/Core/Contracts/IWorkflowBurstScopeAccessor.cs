using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Ambient, explicitly-scoped accessor for the burst-scoped reconstructible cache that currently owns one drain of one
/// workflow execution (ADR 0031 item b, spec 111). Mirrors the push/pop shape of
/// <see cref="IRuntimeLiveDrainDeliveryAccessor"/> and <c>IRuntimeCoalescingSessionAccessor</c>: the scheduler command
/// router establishes a burst scope for the duration of a drain (spanning BOTH the Immediate and Coalesced cadence
/// branches — unlike the cadence-specific live-drain/coalescing accessors), and drain-path consumers resolve it to
/// serve reconstructible reads from memory instead of re-reading durable state on every hop.
/// </summary>
/// <remarks>
/// The accessor is <see cref="AsyncLocal{T}"/>-backed rather than DI-scoped on purpose: a drain-path handler may run in
/// a fresh DI scope while the drain's async flow still surrounds it, so the ambient burst scope must follow the async
/// context, not the DI container scope. When no scope is active (no router established one, or the burst-cache kill
/// switch is off) <see cref="Current"/> is <see langword="null"/> and consumers take the durable path unchanged, which
/// keeps the burst-absent path byte-identical.
/// </remarks>
public interface IWorkflowBurstScopeAccessor
{
    /// <summary>The burst scope owning the current drain, or <see langword="null"/> when none is active.</summary>
    WorkflowBurstScope? Current { get; }

    /// <summary>Pushes a burst scope; dispose the returned handle to pop it.</summary>
    IDisposable Push(WorkflowBurstScope? scope);
}

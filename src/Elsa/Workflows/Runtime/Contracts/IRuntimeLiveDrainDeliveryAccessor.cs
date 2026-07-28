using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Ambient, explicitly-scoped accessor for the live drain that currently owns post-commit intent delivery for an
/// execution (WU-2, spec 105-runtime-live-drain-delivery). Mirrors the push/pop shape of
/// <see cref="IRuntimeCoalescingSessionAccessor"/>: the drain orchestrator establishes a delivery scope for the
/// duration of an Immediate-mode drain, and the post-commit outbox processor resolves it to deliver
/// <c>EnqueueSchedulerWork</c> intents for the owning execution in-memory (idempotent enqueue + direct Delivered mark)
/// instead of taking the durable claim round-trip. When no scope is active (the default outside a live drain, and
/// always on the coalescing path where the overlay session is authoritative) the processor takes the durable claim
/// path unchanged, so recovery sweeps remain the crash backstop.
/// </summary>
public interface IRuntimeLiveDrainDeliveryAccessor
{
    /// <summary>The live-drain delivery scope owning the current drain, or <see langword="null"/> when none is active.</summary>
    RuntimeLiveDrainDeliveryScope? Current { get; }

    /// <summary>Pushes a live-drain delivery scope; dispose the returned handle to pop it.</summary>
    IDisposable Push(RuntimeLiveDrainDeliveryScope? scope);
}

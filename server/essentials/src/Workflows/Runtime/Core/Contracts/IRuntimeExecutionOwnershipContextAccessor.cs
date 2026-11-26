using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Ambient, explicitly-scoped accessor for the current writer's execution ownership lease. Uses a push/pop scope shape:
/// a drain establishes its lease scope for the duration of the drain, and the checkpoint-commit funnel resolves the
/// active lease to fence stale writers out.
/// </summary>
public interface IRuntimeExecutionOwnershipContextAccessor
{
    /// <summary>The lease owning the current execution scope, or <see langword="null"/> when no scope is active.</summary>
    RuntimeExecutionLease? Current { get; }

    /// <summary>Pushes an ownership scope for the supplied lease; dispose the returned handle to pop it.</summary>
    IDisposable Push(RuntimeExecutionLease? lease);
}

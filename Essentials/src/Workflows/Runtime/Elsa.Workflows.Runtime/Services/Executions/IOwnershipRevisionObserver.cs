using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>
/// Is told at which revision of a run's ownership row the row holds a lease.
/// <see cref="RuntimeExecutionOwnershipService"/> tells it after a write that was conditioned on the row's revision
/// has succeeded, so every revision it hears is one at which the row held exactly that lease.
/// </summary>
/// <remarks>
/// The EF checkpoint store registers the one implementation. It remembers the revision so that a fenced commit can
/// check the fence without reading the row. The observer receives the store the row was written through, because
/// only the store knows which rows it writes.
/// </remarks>
internal interface IOwnershipRevisionObserver
{
    /// <summary>The ownership row written through <paramref name="rows"/> holds <paramref name="lease"/> at <paramref name="revision"/>.</summary>
    void LeaseHeld(IExecutionLivenessStateStore rows, RuntimeExecutionLease lease, long revision);

    /// <summary>The holder of <paramref name="lease"/> is giving it up: it is releasing it, or returning a borrow.</summary>
    void LeaseEnded(IExecutionLivenessStateStore rows, RuntimeExecutionLease lease);
}

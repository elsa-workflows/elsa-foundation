using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Publishes the immutable workflow-history physical route admitted during provider startup. The
/// source never carries request scope or operation state; scoped query adapters read the exact same
/// route snapshot after admission succeeds.
/// </summary>
public sealed class GroundworkWorkflowExecutionStatePageRouteSource
{
    private ExecutableStorageRoute? _route;

    public ExecutableStorageRoute Route => Volatile.Read(ref _route)
        ?? throw new InvalidOperationException(
            "Workflow execution history has not been bound to the admitted Groundwork physical route.");

    public void Publish(ExecutableStorageRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var existing = Interlocked.CompareExchange(ref _route, route, null);
        if (existing is not null && !string.Equals(existing.Fingerprint, route.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Workflow execution history is already bound to a different physical route.");
    }
}

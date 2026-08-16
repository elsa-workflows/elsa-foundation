using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Shell-lifetime authority for route generations. The owning child service provider supplies one instance to all
/// scoped route-table facades; no process-global shell key or evictable cache entry participates in correctness.
/// </summary>
internal sealed class RouteTableState
{
    internal object Gate { get; } = new();
    internal RouteGenerationState Current { get; set; } = new(new HttpRouteTableSnapshot(0, []));
}

internal sealed class RouteGenerationState(HttpRouteTableSnapshot snapshot)
{
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _leaseCount;
    private bool _retired;

    internal HttpRouteTableSnapshot Snapshot { get; } = snapshot;

    internal HttpRouteTableSnapshotLease AcquireLease()
    {
        lock (this)
        {
            _leaseCount++;
            return new HttpRouteTableSnapshotLease(Snapshot, _drained.Task, ReleaseLease);
        }
    }

    internal void Retire()
    {
        lock (this)
        {
            _retired = true;
            CompleteIfDrained();
        }
    }

    private void ReleaseLease()
    {
        lock (this)
        {
            if (_leaseCount > 0)
                _leaseCount--;
            CompleteIfDrained();
        }
    }

    private void CompleteIfDrained()
    {
        if (_retired && _leaseCount == 0)
            _drained.TrySetResult();
    }
}

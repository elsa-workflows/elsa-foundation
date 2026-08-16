namespace Elsa.Http.Core.Models;

/// <summary>An immutable, ordered set of workflow HTTP routes published as one generation.</summary>
public sealed class HttpRouteTableSnapshot
{
    public HttpRouteTableSnapshot(long generation, IEnumerable<HttpRouteData> routes)
    {
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        ArgumentNullException.ThrowIfNull(routes);

        Generation = generation;
        Routes = Array.AsReadOnly(routes.ToArray());
    }

    public long Generation { get; }
    public IReadOnlyList<HttpRouteData> Routes { get; }
}

/// <summary>
/// A request-owned lease over an exact route-table generation. The lease's drain task completes once the snapshot
/// has been replaced and this request has released its reference.
/// </summary>
public sealed class HttpRouteTableSnapshotLease : IDisposable
{
    private Action? _release;

    public HttpRouteTableSnapshotLease(HttpRouteTableSnapshot snapshot, Task drained, Action release)
    {
        Snapshot = snapshot;
        Drained = drained;
        _release = release;
    }

    public HttpRouteTableSnapshot Snapshot { get; }
    public Task Drained { get; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

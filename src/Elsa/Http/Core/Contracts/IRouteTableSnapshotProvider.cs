using Elsa.Http.Core.Models;

namespace Elsa.Http.Core.Contracts;

/// <summary>
/// Optional additive route-table seam for request paths that need exact-generation binding and drain. Existing
/// <see cref="IRouteTable"/> implementations remain valid and are used as a compatibility fallback.
/// </summary>
public interface IRouteTableSnapshotProvider
{
    HttpRouteTableSnapshotLease AcquireSnapshot();
}

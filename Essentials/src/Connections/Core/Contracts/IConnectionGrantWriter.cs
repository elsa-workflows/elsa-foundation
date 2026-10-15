using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Writes OAuth grant material as a compare-and-swap on the expected secret version. The distributed
/// lock is an optimization only: correctness comes from this CAS. A losing writer discards its
/// result and re-reads.
/// </summary>
public interface IConnectionGrantWriter
{
    ValueTask<GrantWriteResult> TryCompareAndSwapAsync(GrantWriteRequest request, CancellationToken cancellationToken = default);
}

using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Writes OAuth grant material as a compare-and-swap on the expected secret version. The rotate
/// takes that expected version and only one writer reports <see cref="GrantWriteStatus.Written"/>.
/// A losing writer discards its result and re-reads. The distributed lock is an optimization only.
/// </summary>
public interface IConnectionGrantWriter
{
    ValueTask<GrantWriteResult> TryCompareAndSwapAsync(GrantWriteRequest request, CancellationToken cancellationToken = default);
}

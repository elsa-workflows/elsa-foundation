using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Supplies the finite snapshot of persistence partitions that background infrastructure must visit
/// during one run. Hosts replace the default source when more than the single default partition is used.
/// </summary>
public interface IPersistenceScopeSource
{
    /// <summary>
    /// Returns a finite, duplicate-free snapshot. The runner visits each returned partition exactly once
    /// and never interprets an absent partition as global access.
    /// </summary>
    ValueTask<IReadOnlyList<PersistenceScope>> GetScopesAsync(CancellationToken cancellationToken = default);
}

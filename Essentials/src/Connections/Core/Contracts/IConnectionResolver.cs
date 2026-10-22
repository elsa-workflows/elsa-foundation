using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Resolves a connection at point of use, tenant-scoped and uncached. Owner/run-as is carried from
/// day one; only <see cref="ConnectionOwners.Tenant"/> is valid in v1.
/// </summary>
public interface IConnectionResolver
{
    ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default);
}

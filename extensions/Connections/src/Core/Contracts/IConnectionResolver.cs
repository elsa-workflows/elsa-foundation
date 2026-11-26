using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Resolves a connection at point of use, tenant-scoped and uncached. Owner/run-as is carried from
/// day one; only <see cref="ConnectionOwners.Tenant"/> is valid in v1.
/// Supplied incarnation/revision expectations must be checked against the same document used for the resolved settings.
/// Unknown identities cannot satisfy an incarnation guard; mismatches throw a named connection-unavailable fault.
/// </summary>
public interface IConnectionResolver
{
    ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default);
}

using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Persistence of connection definition and state. The default is in-memory; a durable package
/// replaces it. Lookups are tenant-scoped.
/// </summary>
public interface IConnectionRepository
{
    ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default);

    ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

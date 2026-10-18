using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Persistence of connection definition and state. The default is in-memory; a durable package
/// replaces it. Lookups are tenant-scoped.
/// </summary>
public interface IConnectionRepository
{
    ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks persisted connection fields for a tenant-scoped secret reference, optionally excluding
    /// the connection whose next state the caller is preparing. This is an existence check, not an
    /// atomic guard against a concurrent rebind and delete.
    /// </summary>
    ValueTask<bool> IsSecretReferencedAsync(
        string tenantId,
        string secretName,
        string? excludedConnectionName = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default);

    ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

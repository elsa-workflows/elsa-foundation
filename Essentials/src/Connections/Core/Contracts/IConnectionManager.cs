using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>Lifecycle facade for service connections. Runtime value resolution lives on <see cref="IConnectionResolver"/>.</summary>
public interface IConnectionManager
{
    ValueTask<ConnectionMetadata> CreateAsync(string tenantId, CreateConnectionRequest request, CancellationToken cancellationToken = default);

    ValueTask<ConnectionMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ConnectionMetadata>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default);

    ValueTask<ConnectionMetadata?> UpdateAsync(string tenantId, string name, UpdateConnectionRequest request, CancellationToken cancellationToken = default);

    ValueTask<ConnectionTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    ValueTask<ConnectionMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

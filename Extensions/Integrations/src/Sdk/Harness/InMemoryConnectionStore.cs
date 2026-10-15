using System.Collections.Concurrent;
using Elsa.Integrations.Sdk.Connections;

namespace Elsa.Integrations.Sdk.Harness;

public sealed class InMemoryConnectionStore : IConnectionStore
{
    private readonly ConcurrentDictionary<string, ConnectionRecord> _connections = new(StringComparer.Ordinal);

    public ValueTask<ConnectionRecord?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        _connections.TryGetValue(Compose(tenantId, name), out var connection);
        return ValueTask.FromResult(connection);
    }

    public ValueTask UpsertAsync(ConnectionRecord connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connections[Compose(connection.TenantId, connection.Name)] = connection;
        return ValueTask.CompletedTask;
    }

    private static string Compose(string tenantId, string name) => $"{tenantId}::{name}";
}

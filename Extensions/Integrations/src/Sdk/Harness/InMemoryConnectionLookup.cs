using Elsa.Integrations.Sdk.Connections;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// In-memory <see cref="IConnectionLookup"/> for tests. This is not a second <c>ISecretStore</c>
/// and not <c>Elsa.Connections</c> persistence.
/// </summary>
public sealed class InMemoryConnectionLookup : IConnectionLookup
{
    private readonly Dictionary<(string TenantId, string Name), ConnectionSnapshot> _items = new();

    public void Add(ConnectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _items[(snapshot.TenantId, snapshot.Name)] = snapshot;
    }

    public ValueTask<ConnectionSnapshot?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items.TryGetValue((tenantId, name), out var snapshot);
        return ValueTask.FromResult(snapshot);
    }
}

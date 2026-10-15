namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Tenant-scoped lookup of connection snapshots. Production persistence is <c>Elsa.Connections</c>
/// (plan D6). The testing harness supplies an in-memory implementation. This is not a second
/// <c>ISecretStore</c>.
/// </summary>
public interface IConnectionLookup
{
    ValueTask<ConnectionSnapshot?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

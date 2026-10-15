using Elsa.Secrets.Core.Contracts;

namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Creates a tenant-bound <see cref="IConnectionContext"/>. There is no general tenant service in
/// v1, so the caller (host or harness) supplies the tenant id.
/// </summary>
public sealed class ConnectionContextFactory(
    IConnectionLookup connections,
    IHttpClientFactory httpClientFactory,
    ISecretValueResolver secrets) : IConnectionContextFactory
{
    public IConnectionContext Create(string tenantId) =>
        new ConnectionContext(tenantId, connections, httpClientFactory, secrets);
}

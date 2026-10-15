using Elsa.Activities.Http.Constants;
using Elsa.Secrets.Core.Contracts;

namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Tenant-scoped context. Sits on <c>ISecretValueResolver</c>; it does not resolve or cache secrets
/// itself (#2726 rule 5). The authenticated client is the named Elsa HTTP client (#2726 rule 7).
/// Design-time connection-type descriptors are never loaded here (#2726 rule 8).
/// </summary>
public sealed class ConnectionContext : IConnectionContext
{
    private readonly IConnectionLookup _connections;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretValueResolver _secrets;

    public ConnectionContext(
        string tenantId,
        IConnectionLookup connections,
        IHttpClientFactory httpClientFactory,
        ISecretValueResolver secrets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(secrets);

        TenantId = tenantId.Trim();
        _connections = connections;
        _httpClientFactory = httpClientFactory;
        _secrets = secrets;
    }

    public string TenantId { get; }

    public async ValueTask<IConnectionHandle> ResolveAsync(ConnectionReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var snapshot = await _connections.GetAsync(TenantId, reference.Name, cancellationToken)
            ?? throw new ConnectionUnavailableException(
                $"Connection '{reference.Name}' was not found for tenant '{TenantId}'.",
                reference.Name);

        if (!string.Equals(snapshot.Owner, ConnectionOwners.Tenant, StringComparison.Ordinal))
        {
            throw new ConnectionUnavailableException(
                $"Connection '{reference.Name}' uses owner '{snapshot.Owner}'. Only '{ConnectionOwners.Tenant}' is valid in v1.",
                reference.Name);
        }

        if (reference.TypeName is not null &&
            !string.Equals(snapshot.TypeName, reference.TypeName, StringComparison.Ordinal))
        {
            throw new ConnectionUnavailableException(
                $"Connection '{reference.Name}' is type '{snapshot.TypeName}', not '{reference.TypeName}'.",
                reference.Name);
        }

        var client = _httpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
        return new ConnectionHandle(snapshot, client, _secrets);
    }
}

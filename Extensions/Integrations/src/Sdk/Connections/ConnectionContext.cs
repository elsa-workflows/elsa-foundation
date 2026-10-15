using Elsa.Activities.Http.Constants;
using Elsa.Integrations.Sdk.OAuth;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// STUB tenant-scoped context. Sits on <c>ISecretValueResolver</c>; it does not resolve or cache
/// secrets itself (#2726 rule 5). The authenticated client is the named Elsa HTTP client.
/// </summary>
public sealed class ConnectionContext(
    string tenantId,
    IConnectionStore connections,
    IHttpClientFactory httpClientFactory) : IConnectionContext
{
    public string TenantId { get; } = string.IsNullOrWhiteSpace(tenantId)
        ? throw new ArgumentException("A connection context needs a tenant.", nameof(tenantId))
        : tenantId;

    public async ValueTask<IConnectionHandle> ResolveAsync(ConnectionReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var record = await connections.GetAsync(TenantId, reference.Name, cancellationToken)
            ?? throw new InvalidOperationException($"Connection '{reference.Name}' was not found for tenant '{TenantId}'.");

        var client = httpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
        return new ConnectionHandle(record.TenantId, record.Name, record.Type, record.AuthScheme, record.Settings, client);
    }

    internal static IReadOnlyList<string> BuildAllowlist(ConnectionRecord record)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in record.EgressHostAllowlist)
            AddHost(hosts, host);
        foreach (var key in new[] { "authorizationUrl", "tokenUrl", "scopesUrl" })
        {
            if (record.Settings.TryGetValue(key, out var url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                hosts.Add(uri.IdnHost);
        }

        if (record.Settings.TryGetValue("apiHostAllowlist", out var extra) && !string.IsNullOrWhiteSpace(extra))
        {
            foreach (var host in extra.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                AddHost(hosts, host);
        }

        return [.. hosts];
    }

    private static void AddHost(HashSet<string> hosts, string host)
    {
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
            hosts.Add(uri.IdnHost);
        else if (!string.IsNullOrWhiteSpace(host))
            hosts.Add(host.Trim());
    }
}

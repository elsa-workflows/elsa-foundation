namespace Elsa.Integrations.Sdk.Connections;

internal static class ConnectionEgress
{
    public static void EnsureHostAllowed(HttpRequestMessage request, IConnectionHandle connection)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.EgressAllowlist.Count == 0)
            return;

        var host = request.RequestUri?.IdnHost ?? request.RequestUri?.Host;
        if (string.IsNullOrWhiteSpace(host) || !IsAllowed(host, connection.EgressAllowlist))
        {
            throw new ConnectionUnavailableException(
                $"Connection '{connection.ConnectionName}' refuses host '{host ?? "(none)"}'.",
                connection.ConnectionName);
        }
    }

    private static bool IsAllowed(string host, IReadOnlyList<string> allowlist)
    {
        foreach (var entry in allowlist)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var pattern = entry.Trim();
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..];
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            if (string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Http;

internal sealed class HttpConnectionApplication
{
    public required string ConnectionName { get; init; }
    public required AllowedHostsPolicy Hosts { get; init; }
    public required bool AllowInsecureHttp { get; init; }
    public HashSet<string> HeaderNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? QueryName { get; set; }
}

internal static class HttpConnectionRequestOptionKeys
{
    public static readonly HttpRequestOptionsKey<HttpConnectionApplication> Applied = new("Elsa.Connections.Applied");
}

/// <summary>
/// Narrow public helper for first-party authenticators that attach credentials to an outbound request.
/// </summary>
public static class HttpConnectionCredentials
{
    public static void EnsureHostAllowed(HttpRequestMessage request, IResolvedConnection connection) =>
        HttpConnectionHostGuard.EnsureAllowed(request, connection);

    public static void SetAuthorization(HttpRequestMessage request, IResolvedConnection connection, string value) =>
        HttpConnectionHostGuard.SetAuthorization(request, connection, value);
}

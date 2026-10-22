using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Http;

public sealed class HttpConnectionApplication
{
    public required string ConnectionName { get; init; }
    public required AllowedHostsPolicy Hosts { get; init; }
    public required bool AllowInsecureHttp { get; init; }
    public HashSet<string> HeaderNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? QueryName { get; set; }
}

public static class HttpConnectionRequestOptionKeys
{
    public static readonly HttpRequestOptionsKey<HttpConnectionApplication> Applied = new("Elsa.Connections.Applied");
}

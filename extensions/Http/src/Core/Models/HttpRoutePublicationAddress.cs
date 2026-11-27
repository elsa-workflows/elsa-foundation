namespace Elsa.Http.Core.Models;

/// <summary>Resolves an endpoint-relative workflow route to its externally published HTTP path.</summary>
public static class HttpRoutePublicationAddress
{
    public static bool IsEnabled(string? basePath) => NormalizeBasePath(basePath) is not null;

    public static bool TryResolve(string? basePath, string route, out string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var normalizedBasePath = NormalizeBasePath(basePath);
        if (normalizedBasePath is null)
        {
            address = string.Empty;
            return false;
        }

        address = $"{normalizedBasePath}/{route.Trim().Trim('/')}";
        return true;
    }

    private static string? NormalizeBasePath(string? basePath)
    {
        var normalized = basePath?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "/")
            return null;

        return "/" + normalized.Trim('/');
    }
}

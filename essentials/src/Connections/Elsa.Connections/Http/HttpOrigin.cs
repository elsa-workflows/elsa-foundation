namespace Elsa.Connections.Http;

/// <summary>
/// Origin comparison for http connections and redirects: scheme, host, and effective port.
/// </summary>
internal static class HttpOrigin
{
    public static bool IsCrossOrigin(Uri? previous, Uri next)
    {
        if (previous is null)
            return false;

        return !string.Equals(previous.Scheme, next.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previous.IdnHost, next.IdnHost, StringComparison.OrdinalIgnoreCase)
            || previous.Port != next.Port;
    }
}

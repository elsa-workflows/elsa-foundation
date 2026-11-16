namespace Elsa.Etl.Runtime.Bindings;

/// <summary>Shared header constraints for binding approval and per-request authentication.</summary>
internal static class EtlHttpHeaderPolicy
{
    public static bool IsReservedHeader(string name) => IsTransportReservedHeader(name) ||
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Accept", StringComparison.OrdinalIgnoreCase);

    public static bool IsTransportReservedHeader(string name) =>
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Expect", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase);

    public static bool ValidHeaderName(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~');
}

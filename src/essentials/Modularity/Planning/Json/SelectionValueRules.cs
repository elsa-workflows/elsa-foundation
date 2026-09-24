namespace Elsa.Modularity.Planning.Json;

internal static class SelectionValueRules
{
    public static bool IsDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    // Package identities, evidence sources and resource references are labels, never connection values.
    public static bool IsSafeReference(string? value) => !string.IsNullOrEmpty(value) &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' or '/' or '+');
}

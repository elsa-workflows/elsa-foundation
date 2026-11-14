namespace Elsa.Weaver.Core.Services;

/// <summary>
/// Redacts sensitive literals from text that reaches a log sink or a client-facing error (issue #414 item 3).
/// Each caller supplies all sensitive literals for its boundary; the helper contains no boundary-specific inventory.
/// Redaction fidelity is a security property: narrowing the supplied set can disclose sensitive text.
/// </summary>
public static class WeaverLogRedaction
{
    /// <summary>
    /// Returns <paramref name="message"/> with every non-blank secret in <paramref name="secrets"/>
    /// replaced by <c>[redacted]</c> (ordinal, case-sensitive — secrets are matched verbatim). When
    /// <paramref name="message"/> is null or whitespace, <paramref name="fallback"/> is returned
    /// unchanged (it is a fixed, secret-free diagnostic string).
    /// </summary>
    public static string Redact(string? message, IEnumerable<string?> secrets, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? fallback : message;
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrWhiteSpace(secret))
                normalized = normalized.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        return normalized;
    }
}

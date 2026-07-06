namespace Elsa.Agent.Core.Services;

/// <summary>
/// Single source of truth for redacting provider secrets from text that reaches a log sink or a
/// client-facing error (issue #414 item 3). The provider adapters (Anthropic, GitHub Copilot) each
/// hold a different set of secrets — Anthropic redacts one resolved API key; GitHub Copilot redacts a
/// configured token, a runtime-connection token, and an environment-variable token — so this helper is
/// parameterized by the secret set rather than hard-coding a fixed list. Redaction fidelity is a
/// security property: a narrowed secret set is a leak, so each caller passes its full set explicitly.
/// </summary>
public static class AgentLogRedaction
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

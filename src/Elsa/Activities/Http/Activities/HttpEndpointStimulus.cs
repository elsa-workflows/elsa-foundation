using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// Derives the stimulus identity of an <see cref="HttpEndpoint"/> start trigger (W16, on the W7 seam). It maps an
/// endpoint's <c>(template, method)</c> pair to the opaque <c>(StimulusType, StimulusHash)</c> routing pair the
/// engine already uses — it does not invent a second routing key. Both the publish-time trigger extractor (via
/// <see cref="HttpEndpointTriggerStimulusProvider"/>) and the request middleware that raises the stimulus for an
/// inbound request derive the hash the same way here, so a published endpoint and a matching request resolve to
/// the same routing key.
/// </summary>
/// <remarks>
/// The hash is computed over the normalized route template <em>and</em> the lowercased HTTP method (spec 089 B):
/// a node that declares several supported methods yields one descriptor — and therefore one routing key — per
/// method, so a request only matches the method it was published for. Template normalization is case- and
/// slash-insensitive on <em>literal</em> segments and parameter names, so <c>orders/{Id}</c> and
/// <c>Orders/{id}</c> are the same route: <b>template parameters are case-insensitive by normalization</b>. It
/// deliberately does <b>not</b> touch inline constraint bodies (<c>{code:regex(^[A-Z]+$)}</c>) or default values
/// (<c>{id=ABC}</c>) — those are case-significant to the route matcher, so lowercasing them would corrupt the
/// route (issue #592 item 3). The shared vocabulary (stimulus type, metadata keys) lives on
/// <see cref="HttpEndpointRouting"/>; this class re-exposes the stimulus type for local readability but the value
/// originates there.
/// </remarks>
public static partial class HttpEndpointStimulus
{
    /// <summary>The stimulus type shared by every HTTP endpoint trigger. Delegates to <see cref="HttpEndpointRouting.StimulusType"/>.</summary>
    public const string StimulusType = HttpEndpointRouting.StimulusType;

    /// <summary>
    /// Normalizes an endpoint route template so equivalent routes hash identically regardless of case or
    /// surrounding slashes. Trims whitespace and leading/trailing '/', then lowercases only the case-insensitive
    /// facets: literal text (outside <c>{}</c>) and parameter names (inside <c>{}</c>, up to the first
    /// <c>:</c>/<c>=</c>/<c>?</c> delimiter). Inline constraint bodies and default values are preserved verbatim —
    /// they are case-significant to the route matcher (issue #592 item 3). Throws on null/whitespace.
    /// </summary>
    public static string NormalizeTemplate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.Trim().Trim('/');

        // Fast path: no parameters means the whole template is literal and lowercases wholesale.
        if (!trimmed.Contains('{'))
            return trimmed.ToLowerInvariant();

        // Lowercase literal text (outside {}) and each parameter's name (inside {} up to the first :/=/? delimiter);
        // preserve constraint bodies ({code:regex(^[A-Z]+$)}) and default values ({id=ABC}) verbatim (item 3).
        return ParameterSegment().Replace(LowercaseOutsideBraces(trimmed), match => NormalizeParameter(match.Value));
    }

    // A single {...} parameter segment (no nested braces — inline constraints like regex(...) use parentheses).
    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex ParameterSegment();

    /// <summary>Lowercases every character outside <c>{}</c> braces; brace contents pass through untouched here.</summary>
    private static string LowercaseOutsideBraces(string template)
    {
        var builder = new StringBuilder(template.Length);
        var insideBraces = false;

        foreach (var character in template)
        {
            if (character == '{') insideBraces = true;
            else if (character == '}') insideBraces = false;

            builder.Append(insideBraces || character is '{' or '}' ? character : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Lowercases a <c>{...}</c> parameter's name (case-insensitive to the matcher) but preserves its inline
    /// constraint and default body verbatim: <c>{Code:regex(^[A-Z]+$)}</c> → <c>{code:regex(^[A-Z]+$)}</c>,
    /// <c>{Id=ABC}</c> → <c>{id=ABC}</c>, <c>{*Rest}</c> → <c>{*rest}</c>.
    /// </summary>
    private static string NormalizeParameter(string segment)
    {
        var inner = segment[1..^1]; // strip { }
        var delimiter = inner.IndexOfAny([':', '=', '?']);
        if (delimiter < 0)
            return "{" + inner.ToLowerInvariant() + "}";

        var name = inner[..delimiter].ToLowerInvariant();
        var rest = inner[delimiter..]; // keep constraint/default/optional marker verbatim
        return "{" + name + rest + "}";
    }

    /// <summary>
    /// Computes the deterministic stimulus hash for an endpoint's <c>(template, method)</c> pair. The hash is a
    /// SHA-256 digest formatted with the engine's <c>sha256:</c> prefix (lowercase hex), taken over
    /// <c>"{normalizedTemplate}\n{lowercasedMethod}"</c>, so it is stable across processes and machines and
    /// distinct per method.
    /// </summary>
    public static string Hash(string template, string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var payload = $"{NormalizeTemplate(template)}\n{method.ToLowerInvariant()}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    /// <summary>
    /// Builds one trigger stimulus descriptor per supported method, each carrying the normalized template and
    /// lowercased method on <see cref="TriggerStimulusDescriptor.Metadata"/> via the
    /// <see cref="HttpEndpointRouting.TemplateMetadataKey"/> and <see cref="HttpEndpointRouting.MethodMetadataKey"/>
    /// keys. Methods are deduped case-insensitively and emitted in a deterministic (lowercased ordinal) order so
    /// republishing the same endpoint produces a stable binding set.
    /// </summary>
    /// <remarks>
    /// The optional <paramref name="options"/> (authorize/policy/timeout/size-limit, spec 089 C) are stamped onto
    /// every descriptor's metadata under the <see cref="HttpEndpointRouting"/> option keys, but they are
    /// <b>non-identity</b>: they never enter <see cref="Hash"/>, so two endpoints that differ only in options
    /// resolve to the same routing key. Absent/default options are omitted from the metadata to keep bindings lean.
    /// </remarks>
    public static IReadOnlyCollection<TriggerStimulusDescriptor> Describe(string path, IReadOnlyCollection<string> methods, HttpEndpointStimulusOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(methods);

        var template = NormalizeTemplate(path);
        var optionMetadata = (options ?? HttpEndpointStimulusOptions.None).ToMetadata();
        var normalizedMethods = methods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .Select(method => method.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(method => method, StringComparer.Ordinal)
            .ToArray();

        return normalizedMethods
            .Select(method =>
            {
                var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [HttpEndpointRouting.TemplateMetadataKey] = template,
                    [HttpEndpointRouting.MethodMetadataKey] = method
                };

                foreach (var (key, value) in optionMetadata)
                    metadata[key] = value;

                return new TriggerStimulusDescriptor(StimulusType, Hash(template, method), metadata: metadata);
            })
            .ToArray();
    }
}

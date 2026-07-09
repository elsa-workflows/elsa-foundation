using System.Security.Cryptography;
using System.Text;
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
/// slash-insensitive, and it lowercases parameter names inside <c>{}</c> too, so <c>orders/{Id}</c> and
/// <c>Orders/{id}</c> are the same route: <b>template parameters are case-insensitive by normalization</b>.
/// The shared vocabulary (stimulus type, metadata keys) lives on <see cref="HttpEndpointRouting"/>; this class
/// re-exposes the stimulus type for local readability but the value originates there.
/// </remarks>
public static class HttpEndpointStimulus
{
    /// <summary>The stimulus type shared by every HTTP endpoint trigger. Delegates to <see cref="HttpEndpointRouting.StimulusType"/>.</summary>
    public const string StimulusType = HttpEndpointRouting.StimulusType;

    /// <summary>
    /// Normalizes an endpoint route template so equivalent routes hash identically regardless of case or
    /// surrounding slashes. Trims whitespace, trims leading/trailing '/', and lowercases the whole template
    /// (including parameter names inside <c>{}</c>, making template parameters case-insensitive). Throws on
    /// null/whitespace.
    /// </summary>
    public static string NormalizeTemplate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Trim().Trim('/').ToLowerInvariant();
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

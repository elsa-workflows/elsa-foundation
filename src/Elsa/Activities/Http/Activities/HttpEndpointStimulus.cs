using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// Derives the stimulus identity of an <see cref="HttpEndpoint"/> start trigger (W16, on the W7 seam). It maps
/// an endpoint path to the opaque <c>(StimulusType, StimulusHash)</c> routing pair the engine already uses — it
/// does not invent a second routing key. Both the publish-time trigger extractor (via
/// <see cref="HttpEndpointTriggerStimulusProvider"/>) and the request middleware that raises the stimulus for an
/// inbound request derive the hash the same way here, so a published endpoint and a matching request resolve to
/// the same routing key.
/// </summary>
/// <remarks>
/// The hash is computed over the <em>normalized path only</em> (lowercased, slash-trimmed), not the HTTP
/// method: the W7 provider seam yields a single descriptor per node, and an endpoint may declare several
/// supported methods. Method is carried on the request model for observability and is enforced by the workflow
/// or a follow-up; per-method routing keys are a named follow-up ("HTTP endpoint per-method routing").
/// </remarks>
public static class HttpEndpointStimulus
{
    /// <summary>The stimulus type shared by every HTTP endpoint trigger.</summary>
    public const string StimulusType = "HttpEndpoint";

    /// <summary>Normalizes an endpoint path so the same route hashes identically regardless of case or surrounding slashes.</summary>
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Trim().Trim('/').ToLowerInvariant();
    }

    /// <summary>
    /// Computes the deterministic stimulus hash for an endpoint path. The hash is a SHA-256 digest formatted
    /// with the engine's <c>sha256:</c> prefix; the normalized path is the sole input so the value is stable
    /// across processes and machines.
    /// </summary>
    public static string Hash(string path)
    {
        var normalized = NormalizePath(path);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    /// <summary>Builds the trigger stimulus descriptor for an endpoint path.</summary>
    public static TriggerStimulusDescriptor Describe(string path) =>
        new(StimulusType, Hash(path));
}

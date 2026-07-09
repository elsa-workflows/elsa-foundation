using System.Globalization;
using Elsa.Http.Core;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// The non-identity endpoint options an <see cref="HttpEndpoint"/> stamps onto its trigger-binding metadata
/// (spec 089 sub-unit C): authorization flag/policy, per-request timeout, and per-request body size limit. These
/// ride the binding metadata for the middleware to read, but they never participate in
/// <see cref="HttpEndpointStimulus.Hash"/> — two endpoints that differ only in options share a routing key.
/// </summary>
/// <remarks>
/// <see cref="ToMetadata"/> is the single place that formats these values, keeping the wire encoding invariant and
/// consistent between the describe and read sides. Defaults are omitted from the metadata to keep bindings lean:
/// <c>Authorize == false</c>, a null <c>Policy</c>, a null <c>RequestTimeout</c>, and a null <c>RequestSizeLimit</c>
/// each contribute no key.
/// </remarks>
public sealed record HttpEndpointStimulusOptions(
    bool Authorize = false,
    string? Policy = null,
    TimeSpan? RequestTimeout = null,
    long? RequestSizeLimit = null)
{
    /// <summary>An options value with every option at its default (contributes no metadata).</summary>
    public static readonly HttpEndpointStimulusOptions None = new();

    /// <summary>
    /// Projects the non-default options to their invariant metadata encoding: <c>authorize</c> as
    /// <c>"true"</c> (omitted when false), <c>policy</c> as the raw string, <c>requestTimeout</c> as an invariant
    /// <c>TimeSpan</c> <c>"c"</c> string (e.g. <c>00:00:30</c>), and <c>requestSizeLimit</c> as an invariant
    /// <c>long</c> string.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Authorize)
            metadata[HttpEndpointRouting.AuthorizeMetadataKey] = "true";

        if (!string.IsNullOrWhiteSpace(Policy))
            metadata[HttpEndpointRouting.PolicyMetadataKey] = Policy;

        if (RequestTimeout is { } timeout)
            metadata[HttpEndpointRouting.RequestTimeoutMetadataKey] = timeout.ToString("c", CultureInfo.InvariantCulture);

        if (RequestSizeLimit is { } sizeLimit)
            metadata[HttpEndpointRouting.RequestSizeLimitMetadataKey] = sizeLimit.ToString(CultureInfo.InvariantCulture);

        return metadata;
    }
}

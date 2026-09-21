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
/// <see cref="ToMetadata"/> (write) and <see cref="FromMetadata"/> (read) are paired here as the single owner of
/// the wire encoding, so the describe side and the middleware read side cannot drift (#592 item 14). Defaults are
/// omitted from the metadata to keep bindings lean: <c>Authorize == false</c>, a null <c>Policy</c>, a null
/// <c>RequestTimeout</c>, and a null <c>RequestSizeLimit</c> each contribute no key; reading absent keys yields the
/// same defaults, so <c>FromMetadata(ToMetadata()) == this</c> round-trips.
/// </remarks>
public sealed record HttpEndpointStimulusOptions(
    bool Authorize = false,
    string? Policy = null,
    TimeSpan? RequestTimeout = null,
    long? RequestSizeLimit = null,
    ResponseMode ResponseMode = ResponseMode.Async)
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

        // Omit the response mode when Async (the default) so an async endpoint round-trips to the same empty
        // metadata as before E landed (spec 089 E-D1). Only a Sync endpoint contributes a key.
        if (ResponseMode != ResponseMode.Async)
            metadata[HttpEndpointRouting.ResponseModeMetadataKey] = ResponseMode.ToString();

        return metadata;
    }

    /// <summary>
    /// Fail-closed merge of the endpoint options across every waiting bookmark that matches a resume-only
    /// (template, method) — see <see cref="MergedHttpEndpointOptions"/> and spec 089 D, D-D5. Options are excluded
    /// from the stimulus hash by design, so multiple waiting bookmarks on the same identity can carry <em>divergent</em>
    /// options; enforcing the strictest of each field prevents one lax bookmark from defeating a strict sibling.
    /// <list type="bullet">
    /// <item><c>Authorize</c> is true if ANY bookmark requires it.</item>
    /// <item><c>Policies</c> is the union of all distinct non-empty policies (each must pass — enforced by the caller).</item>
    /// <item><c>RequestSizeLimit</c> is the MIN of the specified limits (an absent limit never relaxes a specified one).</item>
    /// <item><c>RequestTimeout</c> is the MIN of the specified timeouts.</item>
    /// <item><c>ResponseMode</c> is <see cref="ResponseMode.Sync"/> if ANY matching bookmark requests Sync (spec 089
    /// E-D6). The HTTP response is per-request, not per-identity: a sync-authored waiting endpoint must get its
    /// same-exchange reply even when an async sibling shares the (template, method) identity. This is a deliberate
    /// any-Sync rule rather than the fail-closed all-must-agree treatment of the enforcement fields — Sync is the
    /// more capable delivery, and degrading it to 202 (which every Sync dispatch already does on suspend/no-write)
    /// is safe, whereas silently downgrading a sync author's endpoint to async would lose their live response.</item>
    /// </list>
    /// An empty sequence (no waiting bookmark matched) yields <see cref="MergedHttpEndpointOptions.None"/>.
    /// </summary>
    public static MergedHttpEndpointOptions MergeForResume(IEnumerable<IReadOnlyDictionary<string, string>?> metadataPerBookmark)
    {
        var authorizeAny = false;
        var policies = new HashSet<string>(StringComparer.Ordinal);
        long? minSizeLimit = null;
        TimeSpan? minTimeout = null;
        var responseMode = ResponseMode.Async;

        foreach (var metadata in metadataPerBookmark)
        {
            var options = FromMetadata(metadata);
            authorizeAny |= options.Authorize;

            if (!string.IsNullOrWhiteSpace(options.Policy))
                policies.Add(options.Policy);

            if (options.RequestSizeLimit is { } sizeLimit)
                minSizeLimit = minSizeLimit is { } current ? Math.Min(current, sizeLimit) : sizeLimit;

            if (options.RequestTimeout is { } timeout)
                minTimeout = minTimeout is { } current && current <= timeout ? current : timeout;

            if (options.ResponseMode == ResponseMode.Sync)
                responseMode = ResponseMode.Sync;
        }

        return new MergedHttpEndpointOptions(authorizeAny, policies, minSizeLimit, minTimeout, responseMode);
    }

    /// <summary>
    /// Reads the options back from claimant-binding metadata, inverting <see cref="ToMetadata"/> (the middleware
    /// read side, #592 item 14). A null or empty metadata dictionary — and any absent, blank, or malformed key —
    /// yields the corresponding default, so <c>FromMetadata(ToMetadata())</c> is the identity on any well-formed
    /// options value.
    /// </summary>
    public static HttpEndpointStimulusOptions FromMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
            return None;

        return new HttpEndpointStimulusOptions(
            Authorize: metadata.TryGetValue(HttpEndpointRouting.AuthorizeMetadataKey, out var authorize)
                && bool.TryParse(authorize, out var parsedAuthorize) && parsedAuthorize,
            Policy: metadata.GetValueOrDefault(HttpEndpointRouting.PolicyMetadataKey),
            RequestTimeout: metadata.TryGetValue(HttpEndpointRouting.RequestTimeoutMetadataKey, out var timeout)
                && TimeSpan.TryParseExact(timeout, "c", CultureInfo.InvariantCulture, out var parsedTimeout)
                    ? parsedTimeout
                    : null,
            RequestSizeLimit: metadata.TryGetValue(HttpEndpointRouting.RequestSizeLimitMetadataKey, out var sizeLimit)
                && long.TryParse(sizeLimit, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSizeLimit)
                    ? parsedSizeLimit
                    : null,
            // Absent or unparseable ⇒ Async (the default): an endpoint published before E, or a garbled key,
            // preserves today's async/202 contract rather than silently opting into a live write (spec 089 E-D1).
            // Enum.TryParse accepts bare numeric strings (e.g. "99") as undefined values, so IsDefined guards them.
            ResponseMode: metadata.TryGetValue(HttpEndpointRouting.ResponseModeMetadataKey, out var responseMode)
                && Enum.TryParse<ResponseMode>(responseMode, ignoreCase: false, out var parsedResponseMode)
                && Enum.IsDefined(parsedResponseMode)
                    ? parsedResponseMode
                    : ResponseMode.Async);
    }
}

/// <summary>
/// The enforcement-side view of an HTTP endpoint's options once resolved for a request — either from the trigger
/// claimant binding(s) or, for a resume-only match, from the fail-closed merge across all matching waiting bookmarks
/// (<see cref="HttpEndpointStimulusOptions.MergeForResume"/>, spec 089 D, D-D5). It differs from
/// <see cref="HttpEndpointStimulusOptions"/> in one load-bearing way: it carries a <em>set</em> of
/// <see cref="Policies"/> rather than a single policy, because a resume-only match can span several waiting bookmarks
/// each declaring a distinct policy and the middleware must require EVERY distinct policy to pass (fail closed).
/// </summary>
/// <param name="Authorize">Whether authorization must pass before disclosure/dispatch (true if any source requires it).</param>
/// <param name="Policies">The distinct non-empty policies to enforce; empty means an authorize-only check (no named policy).</param>
/// <param name="RequestSizeLimit">The strictest (minimum) per-endpoint body size limit, or null to fall back to the global default.</param>
/// <param name="RequestTimeout">The strictest (minimum) per-endpoint request timeout, or null for no per-endpoint timeout.</param>
/// <param name="ResponseMode">How the resolved endpoint delivers the workflow-authored response (spec 089 E). For a
/// resume-only match this is <see cref="ResponseMode.Sync"/> if ANY matching waiting bookmark requested Sync
/// (<see cref="HttpEndpointStimulusOptions.MergeForResume"/>, E-D6); for the claimant path it is the claimant's own
/// mode (<see cref="FromClaimant"/>). <see cref="None"/> defaults to <see cref="ResponseMode.Async"/>.</param>
public sealed record MergedHttpEndpointOptions(
    bool Authorize,
    IReadOnlyCollection<string> Policies,
    long? RequestSizeLimit,
    TimeSpan? RequestTimeout,
    ResponseMode ResponseMode = ResponseMode.Async)
{
    /// <summary>A merged view with no options set — the resume-only default when no waiting bookmark matched (Async).</summary>
    public static readonly MergedHttpEndpointOptions None = new(false, [], null, null);

    /// <summary>
    /// Projects a single resolved <see cref="HttpEndpointStimulusOptions"/> (the trigger-claimant path) into the
    /// merged enforcement view: a lone policy becomes a one-element <see cref="Policies"/> set, the claimant's
    /// <see cref="HttpEndpointStimulusOptions.ResponseMode"/> is carried through (D-D5 claimant-wins, consistent
    /// with the other fields), and <paramref name="authorizeOverride"/> lets the caller substitute the strongest
    /// Authorize claim across sibling claimants. The claimant path is otherwise unchanged.
    /// </summary>
    public static MergedHttpEndpointOptions FromClaimant(HttpEndpointStimulusOptions options, bool authorizeOverride)
    {
        var policies = string.IsNullOrWhiteSpace(options.Policy)
            ? (IReadOnlyCollection<string>)[]
            : [options.Policy];

        return new MergedHttpEndpointOptions(authorizeOverride, policies, options.RequestSizeLimit, options.RequestTimeout, options.ResponseMode);
    }
}

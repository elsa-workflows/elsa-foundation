using Elsa.Http.Core;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Default <see cref="IHttpEndpointRoutesResolver"/> (spec 089 B, extended for mid-flow resume in D). It unions
/// two sources of endpoint-relative route templates into the per-shell route table:
/// <list type="number">
/// <item>every HTTP-endpoint <b>trigger binding</b> in the durable trigger index (start-capable endpoints), and</item>
/// <item>every waiting, non-expired HTTP-endpoint <b>bookmark</b> (mid-flow endpoints a running instance is
/// suspended on).</item>
/// </list>
/// Both sources carry the route template on the same <see cref="HttpEndpointRouting.TemplateMetadataKey"/>
/// metadata key. Templates are deduped ordinally, so a template published as a start trigger and also awaited by a
/// suspended instance produces a single route. Bindings/bookmarks of another stimulus type, or HTTP entries
/// without a template metadata value, are ignored.
/// </summary>
/// <remarks>
/// <para>
/// Templates are stored <b>endpoint-relative</b> (exactly as authored/normalized, e.g. <c>orders/{id}</c>) —
/// never base-path-prefixed. The endpoints base path is a middleware concern
/// (<c>HttpEndpointMiddleware</c> strips it with segment-bounded matching before consulting the route table),
/// so prefixing here would both duplicate that concern and couple two independently configurable options.
/// </para>
/// <para>
/// <b>Bookmark expiry.</b> Waiting bookmarks are read through the expiry-aware
/// <see cref="IGlobalBookmarkStimulusLookup.FindWaitingByTypeAsync"/> so an expired bookmark's template does not
/// contribute a route — the lookup layer owns expiry filtering (the raw index scan is deliberately unfiltered).
/// A bookmark whose route was already refreshed away but is not yet consumed simply 404s at dispatch (spec 089 D,
/// D-D4 edge case), which is acceptable and self-heals on the next refresh.
/// </para>
/// <para>
/// <b>Conflicts degrade here, they don't brick (issue #592 item 2).</b> Publish-time
/// <c>(template, method)</c> uniqueness across <em>trigger bindings</em> is enforced pre-write by
/// <c>HttpEndpointRoutingUniquenessValidator</c> on the indexer's validation seam, so a healthy store never
/// contains a cross-definition trigger collision. If one nonetheless appears (written out-of-band, or persisted
/// before the validator existed), this resolver only <em>warns</em> and resolves the routes anyway — it also runs
/// at shell startup (<c>UpdateRouteTableStartupTask</c>) and on HTTP-affecting publishes, so throwing would turn
/// one poisoned entry into a host-wide publish outage and a boot failure. The middleware's request-time 409
/// ambiguity guard is the serving backstop for the conflicting endpoint itself. The uniqueness check is
/// <b>trigger-binding-only</b>: a waiting mid-flow bookmark that shares a <c>(template, method)</c> with a
/// published trigger is legal (it is instance-scoped, not a competing definition, spec 089 D-D5), so bookmarks are
/// deliberately exempt from the collision warning.
/// </para>
/// </remarks>
public sealed class HttpEndpointRoutesResolver(
    IWorkflowTriggerBindingStore bindingStore,
    IGlobalBookmarkStimulusLookup bookmarkStimulusLookup,
    ILogger<HttpEndpointRoutesResolver> logger) : IHttpEndpointRoutesResolver
{
    public async ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default)
    {
        // Distinct route templates only: one endpoint publishes one binding per method (all sharing a template),
        // a suspended instance holds one bookmark per method (all sharing a template), two workflows may
        // legitimately register the same template, and a start trigger and a mid-flow bookmark may share one
        // template. Ordinal dedup keeps the route table one entry per concrete path; the ambiguity guard runs at
        // request time, not here.
        var candidates = new Dictionary<string, RouteCandidate>(StringComparer.Ordinal);

        // (1) Trigger bindings. Two bindings that share a (template, method) hash are legitimate when they belong
        // to one definition (republish remnants / a duplicate node); a cross-definition collision is warned about
        // (never thrown — see the class remarks). This uniqueness check applies to trigger bindings only.
        var claimantsByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        var bindings = await bindingStore.ListAllByStimulusTypeAsync(HttpEndpointRouting.StimulusType, cancellationToken);
        foreach (var binding in bindings)
        {
            if (claimantsByHash.TryGetValue(binding.StimulusHash, out var owner))
            {
                if (!StringComparer.Ordinal.Equals(owner, binding.DefinitionId))
                    logger.LogWarning(
                        "The HTTP endpoint '{Endpoint}' is claimed by more than one workflow definition. The publish-time uniqueness validator normally prevents this; requests to this endpoint will be rejected with 409 by the ambiguity guard until one claimant is unpublished.",
                        EndpointRoutingConflictException.DescribeEndpoint(binding));
            }
            else
            {
                claimantsByHash[binding.StimulusHash] = binding.DefinitionId;
            }

            AddTemplate(binding.Metadata, candidates);
        }

        // (2) Union in the templates of every waiting, non-expired HttpEndpoint bookmark (mid-flow suspensions).
        // The lookup returns full BookmarkState snapshots incl. Metadata, from which the durable template is read.
        // Bookmarks are instance-scoped and are NOT run through the cross-definition uniqueness check (D-D5).
        var waiting = await bookmarkStimulusLookup.FindWaitingByTypeAsync(
            new GlobalBookmarkStimulusTypeLookupRequest(HttpEndpointRouting.StimulusType, DateTimeOffset.UtcNow),
            cancellationToken);
        foreach (var bookmark in waiting.Matches)
            AddTemplate(bookmark.Metadata, candidates);

        return candidates.Values.Select(candidate => candidate.ToRouteData()).ToArray();
    }

    private static void AddTemplate(IReadOnlyDictionary<string, string> metadata, IDictionary<string, RouteCandidate> candidates)
    {
        if (!metadata.TryGetValue(HttpEndpointRouting.TemplateMetadataKey, out var template) || string.IsNullOrWhiteSpace(template))
            return;

        if (!candidates.TryGetValue(template, out var candidate))
        {
            candidate = new RouteCandidate(template);
            candidates.Add(template, candidate);
        }

        candidate.Add(metadata);
    }

    private sealed class RouteCandidate(string template)
    {
        private readonly HashSet<string> _methods = new(StringComparer.Ordinal);
        private readonly HashSet<string> _policies = new(StringComparer.Ordinal);
        private bool _wildcardMethod;
        private bool _authorize;

        public void Add(IReadOnlyDictionary<string, string> metadata)
        {
            if (metadata.TryGetValue(HttpEndpointRouting.MethodMetadataKey, out var method) && !string.IsNullOrWhiteSpace(method))
                _methods.Add(method.Trim().ToUpperInvariant());
            else
                _wildcardMethod = true;

            var authorize = metadata.TryGetValue(HttpEndpointRouting.AuthorizeMetadataKey, out var authorizeValue) &&
                            bool.TryParse(authorizeValue, out var parsedAuthorize) && parsedAuthorize;
            _authorize |= authorize;
            if (authorize && metadata.TryGetValue(HttpEndpointRouting.PolicyMetadataKey, out var policy) && !string.IsNullOrWhiteSpace(policy))
                _policies.Add(policy.Trim());
        }

        public HttpRouteData ToRouteData()
        {
            var disposition = _authorize
                ? _policies.Count == 0
                    ? HttpRouteSecurityDispositionMetadata.AuthenticatedPrincipal("Elsa.Http")
                    : HttpRouteSecurityDispositionMetadata.NamedPolicies(_policies, "Elsa.Http")
                : HttpRouteSecurityDispositionMetadata.Public(
                    "workflow-authored",
                    "Workflow-authored HTTP endpoint does not require authorization.");

            return new HttpRouteData(template)
            {
                Methods = _wildcardMethod ? [] : _methods.OrderBy(method => method, StringComparer.Ordinal).ToArray(),
                Metadata = [disposition]
            };
        }
    }
}

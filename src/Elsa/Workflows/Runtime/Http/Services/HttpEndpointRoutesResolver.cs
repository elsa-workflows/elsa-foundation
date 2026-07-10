using Elsa.Http.Core;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;

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
/// </remarks>
public sealed class HttpEndpointRoutesResolver(
    IWorkflowTriggerBindingStore bindingStore,
    IGlobalBookmarkStimulusLookup bookmarkStimulusLookup) : IHttpEndpointRoutesResolver
{
    public async ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default)
    {
        // Distinct route templates only: one endpoint publishes one binding per method (all sharing a template),
        // a suspended instance holds one bookmark per method (all sharing a template), two workflows may
        // legitimately register the same template, and a start trigger and a mid-flow bookmark may share one
        // template. Ordinal dedup keeps the route table one entry per concrete path; the ambiguity guard runs at
        // request time, not here.
        var templates = new HashSet<string>(StringComparer.Ordinal);
        var routes = new List<HttpRouteData>();

        var bindings = await bindingStore.ListByStimulusTypeAsync(HttpEndpointRouting.StimulusType, cancellationToken);
        foreach (var binding in bindings)
            AddTemplate(binding.Metadata, templates, routes);

        // Union in the templates of every waiting, non-expired HttpEndpoint bookmark (mid-flow suspensions). The
        // lookup returns full BookmarkState snapshots incl. Metadata, from which the durable template is read.
        var waiting = await bookmarkStimulusLookup.FindWaitingByTypeAsync(
            new GlobalBookmarkStimulusTypeLookupRequest(HttpEndpointRouting.StimulusType, DateTimeOffset.UtcNow),
            cancellationToken);
        foreach (var bookmark in waiting.Matches)
            AddTemplate(bookmark.Metadata, templates, routes);

        return routes;
    }

    private static void AddTemplate(IReadOnlyDictionary<string, string> metadata, HashSet<string> templates, List<HttpRouteData> routes)
    {
        if (!metadata.TryGetValue(HttpEndpointRouting.TemplateMetadataKey, out var template) || string.IsNullOrWhiteSpace(template))
            return;

        if (!templates.Add(template))
            return;

        routes.Add(new HttpRouteData(template));
    }
}

using Elsa.Http.Core;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Http.Contracts;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Default <see cref="IHttpEndpointRoutesResolver"/> (spec 089 B). Lists every HTTP-endpoint trigger binding
/// from the durable trigger index, reads each binding's <see cref="HttpEndpointRouting.TemplateMetadataKey"/>
/// route template, and projects the distinct templates into <see cref="HttpRouteData"/> for the per-shell route
/// table. Bindings of another stimulus type, or HTTP bindings without a template metadata value, are ignored.
/// </summary>
/// <remarks>
/// Templates are stored <b>endpoint-relative</b> (exactly as authored/normalized, e.g. <c>orders/{id}</c>) —
/// never base-path-prefixed. The endpoints base path is a middleware concern
/// (<c>HttpEndpointMiddleware</c> strips it with segment-bounded matching before consulting the route table),
/// so prefixing here would both duplicate that concern and couple two independently configurable options.
/// </remarks>
public sealed class HttpEndpointRoutesResolver(IWorkflowTriggerBindingStore bindingStore) : IHttpEndpointRoutesResolver
{
    public async ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default)
    {
        var bindings = await bindingStore.ListByStimulusTypeAsync(HttpEndpointRouting.StimulusType, cancellationToken);

        // Distinct route templates only: one endpoint publishes one binding per method, all sharing a template,
        // and two workflows may legitimately register the same template (the ambiguity guard runs at request
        // time, not here). Ordinal dedup keeps the route table one entry per concrete path.
        var templates = new HashSet<string>(StringComparer.Ordinal);
        var routes = new List<HttpRouteData>();

        foreach (var binding in bindings)
        {
            if (!binding.Metadata.TryGetValue(HttpEndpointRouting.TemplateMetadataKey, out var template) || string.IsNullOrWhiteSpace(template))
                continue;

            if (!templates.Add(template))
                continue;

            routes.Add(new HttpRouteData(template));
        }

        return routes;
    }
}

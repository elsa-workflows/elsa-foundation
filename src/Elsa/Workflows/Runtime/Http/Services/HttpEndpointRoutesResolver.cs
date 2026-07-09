using Elsa.Http.Core;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Exceptions;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Default <see cref="IHttpEndpointRoutesResolver"/> (spec 089 B). Lists every HTTP-endpoint trigger binding
/// from the durable trigger index, reads each binding's <see cref="HttpEndpointRouting.TemplateMetadataKey"/>
/// route template, and projects the distinct templates into <see cref="HttpRouteData"/> for the per-shell route
/// table. Bindings of another stimulus type, or HTTP bindings without a template metadata value, are ignored.
/// </summary>
/// <remarks>
/// <para>
/// Templates are stored <b>endpoint-relative</b> (exactly as authored/normalized, e.g. <c>orders/{id}</c>) —
/// never base-path-prefixed. The endpoints base path is a middleware concern
/// (<c>HttpEndpointMiddleware</c> strips it with segment-bounded matching before consulting the route table),
/// so prefixing here would both duplicate that concern and couple two independently configurable options.
/// </para>
/// <para>
/// <b>Publish-time (template, method) uniqueness (issue #592 item 2).</b> This resolver runs full-scan on every
/// publish (through <c>RouteTableTriggerIndexObserver</c>, whose throw fails the publish). A
/// <c>(template, method)</c> pair claimed by more than one workflow <em>definition</em> is an authoring error;
/// resolving throws <see cref="EndpointRoutingConflictException"/> here so the <em>second</em> publish of a
/// conflicting endpoint fails, rather than the collision surfacing only as a request-time 409. The request-time
/// ambiguity guard in the middleware remains a backstop (e.g. a store populated out-of-band).
/// </para>
/// </remarks>
public sealed class HttpEndpointRoutesResolver(IWorkflowTriggerBindingStore bindingStore) : IHttpEndpointRoutesResolver
{
    public async ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default)
    {
        var bindings = await bindingStore.ListByStimulusTypeAsync(HttpEndpointRouting.StimulusType, cancellationToken);

        // Distinct route templates only: one endpoint publishes one binding per method, all sharing a template.
        // Ordinal dedup keeps the route table one entry per concrete path. Two bindings that share a (template,
        // method) hash are legitimate ONLY when they belong to the same definition (republish remnants / a
        // duplicate node); a cross-definition collision fails the publish below.
        var claimantsByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        var templates = new HashSet<string>(StringComparer.Ordinal);
        var routes = new List<HttpRouteData>();

        foreach (var binding in bindings)
        {
            if (claimantsByHash.TryGetValue(binding.StimulusHash, out var owner))
            {
                if (!StringComparer.Ordinal.Equals(owner, binding.DefinitionId))
                    throw new EndpointRoutingConflictException(DescribeConflict(binding));
            }
            else
            {
                claimantsByHash[binding.StimulusHash] = binding.DefinitionId;
            }

            if (!binding.Metadata.TryGetValue(HttpEndpointRouting.TemplateMetadataKey, out var template) || string.IsNullOrWhiteSpace(template))
                continue;

            if (!templates.Add(template))
                continue;

            routes.Add(new HttpRouteData(template));
        }

        return routes;
    }

    private static string DescribeConflict(WorkflowTriggerBinding binding)
    {
        var template = binding.Metadata.GetValueOrDefault(HttpEndpointRouting.TemplateMetadataKey, "(unknown template)");
        var method = binding.Metadata.GetValueOrDefault(HttpEndpointRouting.MethodMetadataKey, "(unknown method)");
        return $"{method.ToUpperInvariant()} {template}";
    }
}

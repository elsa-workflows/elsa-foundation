using Elsa.Http.Core;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Exceptions;
using Microsoft.Extensions.Logging;

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
/// <b>Conflicts degrade here, they don't brick (issue #592 item 2).</b> Publish-time
/// <c>(template, method)</c> uniqueness is enforced pre-write by <c>HttpEndpointRoutingUniquenessValidator</c>
/// on the indexer's validation seam, so a healthy store never contains a cross-definition collision. If one
/// nonetheless appears (written out-of-band, or persisted before the validator existed), this resolver only
/// <em>warns</em> and resolves the routes anyway — it also runs at shell startup
/// (<c>UpdateRouteTableStartupTask</c>) and on every observed publish, so throwing would turn one poisoned
/// entry into a host-wide publish outage and a boot failure. The middleware's request-time 409 ambiguity guard
/// is the serving backstop for the conflicting endpoint itself.
/// </para>
/// </remarks>
public sealed class HttpEndpointRoutesResolver(
    IWorkflowTriggerBindingStore bindingStore,
    ILogger<HttpEndpointRoutesResolver> logger) : IHttpEndpointRoutesResolver
{
    public async ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default)
    {
        var bindings = await bindingStore.ListByStimulusTypeAsync(HttpEndpointRouting.StimulusType, cancellationToken);

        // Distinct route templates only: one endpoint publishes one binding per method, all sharing a template.
        // Ordinal dedup keeps the route table one entry per concrete path. Two bindings that share a (template,
        // method) hash are legitimate when they belong to one definition (republish remnants / a duplicate
        // node); a cross-definition collision is warned about (never thrown — see the class remarks).
        var claimantsByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        var templates = new HashSet<string>(StringComparer.Ordinal);
        var routes = new List<HttpRouteData>();

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

            if (!binding.Metadata.TryGetValue(HttpEndpointRouting.TemplateMetadataKey, out var template) || string.IsNullOrWhiteSpace(template))
                continue;

            if (!templates.Add(template))
                continue;

            routes.Add(new HttpRouteData(template));
        }

        return routes;
    }
}

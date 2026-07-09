using Elsa.Http.Core;
using Elsa.Http.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Default <see cref="IHttpEndpointRoutesResolver"/> (spec 089 B). Lists every HTTP-endpoint trigger binding
/// from the durable trigger index, reads each binding's <see cref="HttpEndpointRouting.TemplateMetadataKey"/>
/// route template, and projects the distinct templates (each prefixed with the shell base path) into
/// <see cref="HttpRouteData"/> for the per-shell route table. Bindings of another stimulus type, or HTTP
/// bindings without a template metadata value, are ignored.
/// </summary>
public sealed class HttpEndpointRoutesResolver(
    IWorkflowTriggerBindingStore bindingStore,
    IOptions<WorkflowsRuntimeHttpFeatureOptions> options) : IHttpEndpointRoutesResolver
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

            routes.Add(new HttpRouteData(PrefixWithBasePath(template)));
        }

        return routes;
    }

    private string PrefixWithBasePath(string template)
    {
        var segments = new[] { options.Value.BasePath, template };
        return segments.JoinSegments();
    }
}

using Elsa.Http.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Http.Services;

internal sealed class HttpEndpointRoutesResolver(IOptions<WorkflowsRuntimeHttpFeatureOptions> options) : IHttpEndpointRoutesResolver
{
    public Task<IEnumerable<HttpRouteData>> GetRoutes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(Enumerable.Empty<HttpRouteData>());

        var routes = new HttpRouteData[] { GetRoute(path) };
        return Task.FromResult(routes.AsEnumerable());
    }

    private HttpRouteData GetRoute(string path)
    {
        var routeSegments = new[] { options.Value.BasePath.ToString(), path };
        var route = routeSegments.JoinSegments();
        return new HttpRouteData(route);
    }
}
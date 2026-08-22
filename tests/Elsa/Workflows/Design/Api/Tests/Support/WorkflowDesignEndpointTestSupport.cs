using Elsa.Workflows.Design.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Tests.Support;

internal static class WorkflowDesignEndpointTestSupport
{
    public static RouteEndpoint[] MapEndpoints()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        WorkflowsDesignApi.MapWorkflowsDesignApi(routes);
        return routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
    }

    public static RouteEndpoint Find(RouteEndpoint[] endpoints, string route, string method) =>
        endpoints.Single(endpoint => endpoint.RoutePattern.RawText == route &&
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) == true);

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

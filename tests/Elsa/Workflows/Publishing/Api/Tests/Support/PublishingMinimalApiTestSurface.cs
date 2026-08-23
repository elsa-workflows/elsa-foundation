using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

internal static class PublishingMinimalApiTestSurface
{
    public static IReadOnlyList<RouteEndpoint> Map()
    {
        using var provider = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new RouteBuilder(provider);
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);
        return routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
    }

    public static RouteEndpoint Named(string endpointName) =>
        Map().Single(endpoint =>
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ==
            $"ElsaWorkflowsPublishingApiEndpoints{endpointName}");

    private sealed class RouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

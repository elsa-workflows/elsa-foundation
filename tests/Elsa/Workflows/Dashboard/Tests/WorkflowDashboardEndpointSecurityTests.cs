using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

public sealed class WorkflowDashboardEndpointSecurityTests
{
    [Theory]
    [InlineData("/_elsa/workflows/dashboard/runs")]
    [InlineData("/_elsa/workflows/dashboard/definitions")]
    public void Dashboard_routes_require_explicit_read_permission_and_are_not_anonymous(string route)
    {
        var endpoint = GetEndpoint(route);

        var owner = Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>());
        Assert.Equal("Elsa.Workflows.Dashboard", owner.OwnerId);
        Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(
            endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        Assert.Equal(EndpointSecurityDispositionKind.Permission, security.Kind);
        var policy = new PermissionPolicyCodec().Parse(security.Value!);
        Assert.Contains(PermissionKey.Normalize(WorkflowsDashboardPermissions.Read), policy.Descriptor!.Permissions);
        Assert.DoesNotContain(endpoint.Metadata, item => item is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute);
    }

    private static RouteEndpoint GetEndpoint(string route)
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(routes);
        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText == route);
        Assert.NotNull(endpoint);
        return endpoint!;
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Tests.Api.Support;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Proves every Activities Design Minimal API route owns exactly one catalog action and publishes
/// the standard framework-neutral security, owner, and authoring metadata.
/// </summary>
public sealed class ActivityDesignEndpointSecurityTests
{
    private const string OwnerId = "Elsa.Activities.Design.Api";

    [Fact]
    public void Every_mapped_route_requires_its_single_catalog_action_and_is_not_anonymous()
    {
        var endpoints = MapEndpoints();

        Assert.Equal(ActivitiesDesignCompatibilityCases.Manifest.Count, endpoints.Count);
        foreach (var route in ActivitiesDesignCompatibilityCases.Manifest)
        {
            var endpoint = Find(endpoints, route);
            var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);
            var descriptor = Assert.IsType<PermissionPolicyDescriptor>(parsed.Descriptor);
            var permission = route.Action switch
            {
                "read" => ActivityDesignPermissions.Read,
                "manage" => ActivityDesignPermissions.Manage,
                _ => throw new InvalidOperationException($"Unexpected manifest action '{route.Action}'.")
            };

            Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
            Assert.Equal(PermissionRequirementMode.Single, descriptor.Mode);
            Assert.Equal([PermissionKey.Normalize(permission)], descriptor.Permissions);
            Assert.DoesNotContain(PermissionKey.Wildcard, descriptor.Permissions);
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());

            var disposition = Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>());
            Assert.Equal(EndpointSecurityDispositionKind.Permission, disposition.Kind);
            var dispositionPolicy = new PermissionPolicyCodec().Parse(disposition.Value!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, dispositionPolicy.Status);
            Assert.Equal([PermissionKey.Normalize(permission)], dispositionPolicy.Descriptor!.Permissions);
            Assert.Equal(OwnerId, Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()).OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, Assert.IsType<EndpointAuthoringMetadata>(endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()).Model);
        }
    }

    [Theory]
    [InlineData("Drafts.ProposeContract")]
    [InlineData("Drafts.ApplyContractProposal")]
    public void Contract_proposal_routes_require_activity_design_manage(string operation)
    {
        var endpoint = Find(MapEndpoints(), ActivitiesDesignCompatibilityCases.Manifest.Single(route => route.Id == operation));
        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);

        Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
        Assert.Equal([PermissionKey.Normalize(ActivityDesignPermissions.Manage)], parsed.Descriptor!.Permissions);
        Assert.Equal([HttpMethods.Post], endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public void Permission_contributor_catalogs_every_route_owned_action_once()
    {
        var permissions = new ActivityDesignPermissionContributor().Contribute().ToArray();

        Assert.Equal(
            [
                ActivityDesignPermissions.AuthorProvider,
                ActivityDesignPermissions.ReadProviderPayload,
                ActivityDesignPermissions.Manage,
                ActivityDesignPermissions.Read
            ],
            permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal));
        Assert.Equal(permissions.Length, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(permissions, permission => permission.Key == PermissionKey.Wildcard);
    }

    private static IReadOnlyList<RouteEndpoint> MapEndpoints()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        ActivitiesDesignApi.MapActivitiesDesignApi(routes);
        return routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
    }

    private static RouteEndpoint Find(IEnumerable<RouteEndpoint> endpoints, ActivityDesignRoute route) =>
        Assert.Single(endpoints, endpoint =>
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is [var method] &&
            new Elsa.Api.Compatibility.Testing.Manifests.EndpointIdentity(endpoint.RoutePattern.RawText!, method) == route.Endpoint);

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

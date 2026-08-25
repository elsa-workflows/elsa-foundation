using Elsa.Api.AspNetCore;
using Elsa.Api.Endpoints.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Elsa.Api.Endpoints.Tests;

/// <summary>
/// Mapping-time coverage for <see cref="ApiEndpointMapper"/>: shape detection, attribute routes and
/// prefixing, Configure on an uninitialized instance, the mapping-time error contract, and
/// attribute- and options-contributed conventions.
/// </summary>
public sealed class ApiEndpointMapperTests
{
    private static (ModuleEndpointGroup Group, TestEndpointRouteBuilder Routes) Group()
    {
        var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        var group = routes.MapModuleEndpoints(
            "Test.Owner",
            new TestJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return (group, routes);
    }

    private static RouteEndpoint Single(TestEndpointRouteBuilder routes) =>
        Assert.IsType<RouteEndpoint>(Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints)));

    [Fact]
    public void Body_shape_maps_route_verb_name_owner_and_request_metadata()
    {
        var (group, routes) = Group();

        group.MapEndpoint<ShapeEndpoints.BodyShape>();

        var endpoint = Single(routes);
        Assert.Equal("/items/{id}", endpoint.RoutePattern.RawText);
        Assert.Equal(["POST"], endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        Assert.Equal("TestOwnerEndpointsBodyShape", endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName);
        Assert.Equal("Test.Owner", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
        var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
        Assert.NotNull(accepts);
        Assert.Equal(typeof(SampleBody), accepts!.RequestType);
        Assert.Contains("application/json", accepts.ContentTypes);
        var success = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(SampleResponse), success.Type);
    }

    [Fact]
    public void No_content_shape_documents_204_with_a_void_body()
    {
        var (group, routes) = Group();

        group.MapEndpoint<ShapeEndpoints.NoContentShape>();

        var endpoint = Single(routes);
        var success = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status204NoContent);
        Assert.Equal(typeof(void), success.Type);
        Assert.Empty(success.ContentTypes);
    }

    [Fact]
    public void Unbound_shape_declares_no_request_metadata()
    {
        var (group, routes) = Group();

        group.MapEndpoint<ShapeEndpoints.UnboundShape>();

        var endpoint = Single(routes);
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    [Fact]
    public void Route_prefix_is_applied_to_attribute_routes()
    {
        var (group, routes) = Group();

        group.MapEndpoint<ShapeEndpoints.UnboundShape>("api/v1");

        Assert.Equal("api/v1/status", Single(routes).RoutePattern.RawText);
    }

    [Fact]
    public void Configure_runs_on_an_uninitialized_instance_so_constructors_never_run_at_map_time()
    {
        var (group, routes) = Group();

        group.MapEndpoint<ThrowingConstructorEndpoint>();

        Assert.Equal("/uninitialized", Single(routes).RoutePattern.RawText);
    }

    [Fact]
    public void Attribute_and_options_conventions_both_contribute_metadata()
    {
        var (group, routes) = Group();

        group.MapEndpoint<MarkedEndpoint>();

        var markers = Single(routes).Metadata.GetOrderedMetadata<TestMarkerMetadata>();
        Assert.Equal(2, markers.Count);
    }

    [Fact]
    public void A_documented_status_replaces_the_runtime_status_in_metadata()
    {
        var (group, routes) = Group();

        group.MapOperation<SampleBody>(
            "POST", "/documented", "Documented", null, ["application/json"],
            typeof(SampleResponse), StatusCodes.Status201Created, StatusCodes.Status200OK,
            (_, _, _) => Task.CompletedTask);

        var produced = Single(routes).Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produced, metadata => metadata.StatusCode == StatusCodes.Status200OK);
        Assert.DoesNotContain(produced, metadata => metadata.StatusCode == StatusCodes.Status201Created);
    }

    [Fact]
    public void The_shared_401_403_pair_is_always_documented()
    {
        var (group, routes) = Group();

        group.MapEndpoint<ShapeEndpoints.UnboundShape>();

        var produced = Single(routes).Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produced, metadata => metadata.StatusCode == StatusCodes.Status401Unauthorized);
        Assert.Contains(produced, metadata => metadata.StatusCode == StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void An_endpoint_without_a_route_is_rejected_at_map_time()
    {
        var (group, _) = Group();

        var exception = Assert.Throws<InvalidOperationException>(() => group.MapEndpoint<NoRouteEndpoint>());

        Assert.Contains("declares no route", exception.Message);
    }

    [Fact]
    public void An_endpoint_without_an_operation_identifier_is_rejected_at_map_time()
    {
        var (group, _) = Group();

        var exception = Assert.Throws<InvalidOperationException>(() => group.MapEndpoint<NoOperationEndpoint>());

        Assert.Contains("no operation identifier", exception.Message);
    }

    [Fact]
    public void Assembly_scanning_maps_endpoints_in_deterministic_full_name_order()
    {
        var (group, _) = Group();

        var exception = Record.Exception(() => group.MapEndpointsFrom(typeof(ApiEndpointMapperTests).Assembly));

        // The scan includes deliberately invalid fixtures, which throw. Determinism is proven by
        // the failure always naming the ordinal-first type that individually fails to map — the
        // expectation is computed with the mapper's own ordering, so adding or renaming fixtures
        // cannot silently change it.
        var firstInvalid = typeof(ApiEndpointMapperTests).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ApiEndpointBase).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .First(type => !MapsIndividually(type));
        Assert.NotNull(exception);
        Assert.Contains(firstInvalid.FullName!, exception!.Message);
    }

    private static bool MapsIndividually(Type endpointType)
    {
        var (group, _) = Group();
        var method = typeof(ApiEndpointMapper)
            .GetMethod(nameof(ApiEndpointMapper.MapEndpoint))!
            .MakeGenericMethod(endpointType);
        try
        {
            method.Invoke(null, [group, null]);
            return true;
        }
        catch (System.Reflection.TargetInvocationException exception)
            when (exception.InnerException is InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class NoRouteEndpoint : ApiEndpointWithoutRequest<SampleResponse>
    {
        public override void Configure(ApiEndpointOptions options) => options.Operation = "NoRoute";

        public override Task<SampleResponse> HandleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SampleResponse("never"));
    }

    [Get("/no-operation")]
    private sealed class NoOperationEndpoint : ApiEndpointWithoutRequest<SampleResponse>
    {
        public override Task<SampleResponse> HandleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SampleResponse("never"));
    }

    /// <summary>A bare route builder so mapping-time behavior is testable without a host.</summary>
    internal sealed class TestEndpointRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

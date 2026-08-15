using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class EndpointManifestBuilderTests
{
    [Fact]
    public void Captures_normalized_routes_and_sorted_multi_method_metadata()
    {
        var first = BuildDataSource("/API/orders/{orderId:int}/", "orders", ["POST", "get"]);
        var second = BuildDataSource("/api/orders/{different:int}", "orders", ["GET"]);

        var manifest = new EndpointManifestBuilder([second, first]).Build();

        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal("/api/orders/{param:int}", manifest.Entries[0].Route.Value);
        Assert.Equal(["GET"], manifest.Entries[0].Methods);
        Assert.Equal(["GET", "POST"], manifest.Entries[1].Methods);
    }

    [Fact]
    public void Captures_are_byte_stable_across_ten_runs_and_source_order()
    {
        var sources = new[]
        {
            BuildDataSource("/z", "zeta", ["GET"]),
            BuildDataSource("/a", "alpha", ["POST"])
        };

        var expected = new EndpointManifestBuilder(sources.Reverse()).BuildJson();
        for (var index = 0; index < 10; index++)
            Assert.Equal(expected, new EndpointManifestBuilder(sources).BuildJson());
    }

    [Fact]
    public void Tied_semantic_keys_use_canonical_entry_content_as_the_final_order()
    {
        var first = BuildDataSource("/same", "owner", ["GET"], displayName: "zeta");
        var second = BuildDataSource("/same", "owner", ["GET"], displayName: "alpha");

        Assert.Equal(
            new EndpointManifestBuilder([first, second]).BuildJson(),
            new EndpointManifestBuilder([second, first]).BuildJson());
    }

    [Fact]
    public void Rejects_missing_security_metadata_with_route_and_owner_context()
    {
        var dataSource = BuildDataSource("/orders", "orders", ["GET"], addSecurity: false);

        var exception = Assert.Throws<EndpointManifestValidationException>(() => new EndpointManifestBuilder([dataSource]).Build());

        Assert.Contains("missing security disposition", exception.Message);
        Assert.Equal("/orders", exception.Route.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rejects_dispositions_that_contradict_standard_authorization_metadata(bool declaredPublic)
    {
        var disposition = declaredPublic
            ? EndpointSecurityDispositionMetadata.Public("test", "Exercises contradictory metadata.")
            : EndpointSecurityDispositionMetadata.Permission("permission:v1:single:orders.read");
        object contradictoryMetadata = declaredPublic
            ? new AuthorizeAttribute("orders")
            : new AllowAnonymousAttribute();
        var dataSource = BuildDataSource(
            "/orders", "orders", ["GET"], security: disposition, extraMetadata: [contradictoryMetadata]);

        var exception = Assert.Throws<EndpointManifestValidationException>(() =>
            new EndpointManifestBuilder([dataSource]).Build());

        Assert.Contains("conflicts with", exception.Message, StringComparison.Ordinal);
    }

    private static EndpointDataSource BuildDataSource(
        string route,
        string owner,
        string[] methods,
        bool addSecurity = true,
        EndpointSecurityDispositionMetadata? security = null,
        IReadOnlyList<object>? extraMetadata = null,
        string? displayName = null)
    {
        var metadata = new List<object>
        {
            new EndpointOwnershipMetadata(owner),
            new EndpointAuthoringMetadata(EndpointAuthoringModels.MinimalApi),
            new HttpMethodMetadata(methods)
        };
        if (addSecurity)
        {
            var disposition = security ?? EndpointSecurityDispositionMetadata.Public("test", "Test fixture is intentionally public.");
            metadata.Add(disposition);
            if (disposition.Kind == EndpointSecurityDispositionKind.Public)
                metadata.Add(new AllowAnonymousAttribute());
        }
        if (extraMetadata is not null)
            metadata.AddRange(extraMetadata);
        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            0,
            new EndpointMetadataCollection(metadata),
            displayName ?? $"{owner}:{route}");
        return new TestEndpointDataSource([endpoint]);
    }

    private sealed class TestEndpointDataSource(IReadOnlyList<Endpoint> endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints => endpoints;
        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}

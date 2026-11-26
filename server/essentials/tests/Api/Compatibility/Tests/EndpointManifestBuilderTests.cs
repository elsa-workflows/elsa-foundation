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
    public void Reconstructs_programmatically_authored_route_patterns_without_using_object_stringification()
    {
        var pattern = RoutePatternFactory.Pattern(
            RoutePatternFactory.Segment(RoutePatternFactory.LiteralPart("orders")),
            RoutePatternFactory.Segment(RoutePatternFactory.ParameterPart("id")));
        var endpoint = BuildEndpoint(pattern, "orders", ["GET"]);

        var manifest = new EndpointManifestBuilder([new TestEndpointDataSource([endpoint])]).Build();

        Assert.Equal("/orders/{param}", manifest.Entries.Single().Route.Value);
    }

    [Fact]
    public void Uses_public_route_diagnostics_metadata_for_non_route_endpoints()
    {
        var metadata = StandardMetadata("diagnostics", ["GET"]);
        metadata.Add(new TestRouteDiagnosticsMetadata("/diagnostics"));
        var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "diagnostics");

        var manifest = new EndpointManifestBuilder([new TestEndpointDataSource([endpoint])]).Build();

        Assert.Equal("/diagnostics", manifest.Entries.Single().Route.Value);
    }

    [Fact]
    public void Rejects_missing_security_metadata_with_route_and_owner_context()
    {
        var dataSource = BuildDataSource("/orders", "orders", ["GET"], addSecurity: false);

        var exception = Assert.Throws<EndpointManifestValidationException>(() => new EndpointManifestBuilder([dataSource]).Build());

        Assert.Contains("missing security disposition", exception.Message);
        Assert.Equal("/orders", exception.Route.Value);
    }

    [Fact]
    public void Accepts_host_credential_disposition_enforced_by_a_custom_endpoint_filter()
    {
        var metadata = BaseMetadata("Elsa.Workbench", ["POST"]);
        metadata[0] = EndpointOwnershipMetadata.Host("Elsa.Workbench");
        metadata.Add(EndpointSecurityDispositionMetadata.HostCredential(
            "X-Elsa-Module-Management-Key",
            "Elsa.Workbench"));
        metadata.Add(new EndpointHostCredentialEnforcementMetadata(
            "X-Elsa-Module-Management-Key",
            "Elsa.Workbench"));

        var endpoint = BuildEndpoint(
            RoutePatternFactory.Parse("/_admin/shells/reload-all"),
            "Elsa.Workbench",
            ["POST"],
            metadata);

        var manifest = new EndpointManifestBuilder([new TestEndpointDataSource([endpoint])]).Build();

        var entry = Assert.Single(manifest.Entries);
        Assert.Equal(EndpointSecurityDispositionKind.HostCredential, entry.SecurityDisposition?.Kind);
        Assert.Equal(EndpointOwnerKind.Host, entry.OwnerKind);
    }

    [Fact]
    public void Rejects_duplicate_host_credential_enforcement_markers_with_route_context()
    {
        var metadata = BaseMetadata("Elsa.Workbench", ["POST"]);
        metadata.Add(EndpointSecurityDispositionMetadata.HostCredential("X-Key", "Elsa.Workbench"));
        metadata.Add(new EndpointHostCredentialEnforcementMetadata("X-Key", "Elsa.Workbench"));
        metadata.Add(new EndpointHostCredentialEnforcementMetadata("X-Key", "Elsa.Workbench"));

        var exception = Assert.Throws<EndpointManifestValidationException>(() =>
            new EndpointManifestBuilder([new TestEndpointDataSource([
                BuildEndpoint(RoutePatternFactory.Parse("/_admin/duplicate"), "Elsa.Workbench", ["POST"], metadata)])]).Build());

        Assert.Contains("ambiguous host credential enforcement metadata", exception.Problems);
        Assert.Contains("POST /_admin/duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_host_credential_marker_with_non_host_credential_disposition()
    {
        var metadata = BaseMetadata("Elsa.Workbench", ["GET"]);
        metadata.Add(EndpointSecurityDispositionMetadata.NamedPolicy("Default", "Elsa.Workbench"));
        metadata.Add(new EndpointHostCredentialEnforcementMetadata("X-Key", "Elsa.Workbench"));
        metadata.Add(new AuthorizeAttribute("Default"));

        var exception = Assert.Throws<EndpointManifestValidationException>(() =>
            new EndpointManifestBuilder([new TestEndpointDataSource([
                BuildEndpoint(RoutePatternFactory.Parse("/_admin/non-host"), "Elsa.Workbench", ["GET"], metadata)])]).Build());

        Assert.Contains("host credential enforcement metadata conflicts with security disposition", exception.Problems);
        Assert.Contains("GET /_admin/non-host", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_host_credential_marker_with_mismatched_credential_or_owner()
    {
        var metadata = BaseMetadata("Elsa.Workbench", ["POST"]);
        metadata.Add(EndpointSecurityDispositionMetadata.HostCredential("X-Expected", "Elsa.Workbench"));
        metadata.Add(new EndpointHostCredentialEnforcementMetadata("X-Actual", "Other.Host"));

        var exception = Assert.Throws<EndpointManifestValidationException>(() =>
            new EndpointManifestBuilder([new TestEndpointDataSource([
                BuildEndpoint(RoutePatternFactory.Parse("/_admin/mismatch"), "Elsa.Workbench", ["POST"], metadata)])]).Build());

        Assert.Contains("host credential enforcement metadata conflicts with host credential disposition", exception.Problems);
        Assert.Contains("POST /_admin/mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EndpointSecurityDispositionKind.HostCredential, "X-Key")]
    [InlineData(EndpointSecurityDispositionKind.NamedPolicy, "Default")]
    public void Rejects_owner_bearing_security_disposition_that_conflicts_with_endpoint_owner(
        EndpointSecurityDispositionKind kind,
        string value)
    {
        var metadata = BaseMetadata("Elsa.Workbench", ["GET"]);
        metadata.Add(kind == EndpointSecurityDispositionKind.HostCredential
            ? EndpointSecurityDispositionMetadata.HostCredential(value, "Other.Host")
            : EndpointSecurityDispositionMetadata.NamedPolicy(value, "Other.Host"));
        if (kind == EndpointSecurityDispositionKind.HostCredential)
            metadata.Add(new EndpointHostCredentialEnforcementMetadata(value, "Other.Host"));
        else
            metadata.Add(new AuthorizeAttribute(value));

        var exception = Assert.Throws<EndpointManifestValidationException>(() =>
            new EndpointManifestBuilder([new TestEndpointDataSource([
                BuildEndpoint(RoutePatternFactory.Parse("/_admin/owner-conflict"), "Elsa.Workbench", ["GET"], metadata)])]).Build());

        Assert.Contains("security disposition owner conflicts with endpoint owner", exception.Problems);
        Assert.Contains("GET /_admin/owner-conflict", exception.Message, StringComparison.Ordinal);
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
        var metadata = BaseMetadata(owner, methods);
        if (addSecurity)
        {
            var disposition = security ?? EndpointSecurityDispositionMetadata.Public("test", "Test fixture is intentionally public.");
            metadata.Add(disposition);
            if (disposition.Kind == EndpointSecurityDispositionKind.Public)
                metadata.Add(new AllowAnonymousAttribute());
        }
        if (extraMetadata is not null)
            metadata.AddRange(extraMetadata);
        var endpoint = BuildEndpoint(RoutePatternFactory.Parse(route), owner, methods, metadata, displayName);
        return new TestEndpointDataSource([endpoint]);
    }

    private static List<object> BaseMetadata(string owner, string[] methods) =>
    [
        new EndpointOwnershipMetadata(owner),
        new EndpointAuthoringMetadata(EndpointAuthoringModels.MinimalApi),
        new HttpMethodMetadata(methods)
    ];

    private static List<object> StandardMetadata(string owner, string[] methods)
    {
        var metadata = BaseMetadata(owner, methods);
        metadata.Add(EndpointSecurityDispositionMetadata.Public("test", "Test fixture is intentionally public."));
        metadata.Add(new AllowAnonymousAttribute());
        return metadata;
    }

    private static RouteEndpoint BuildEndpoint(RoutePattern pattern, string owner, string[] methods,
        IReadOnlyList<object>? metadata = null, string? displayName = null) => new(
            _ => Task.CompletedTask,
            pattern,
            0,
            new EndpointMetadataCollection(metadata ?? StandardMetadata(owner, methods)),
            displayName ?? $"{owner}:{pattern.RawText ?? "programmatic"}");

    private sealed record TestRouteDiagnosticsMetadata(string Route) : IRouteDiagnosticsMetadata;

    private sealed class TestEndpointDataSource(IReadOnlyList<Endpoint> endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints => endpoints;
        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}

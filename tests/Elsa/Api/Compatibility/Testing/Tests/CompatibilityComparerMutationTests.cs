using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class CompatibilityComparerMutationTests
{
    [Theory]
    [InlineData("binding")]
    [InlineData("json")]
    [InlineData("status")]
    [InlineData("media-types")]
    [InlineData("headers")]
    [InlineData("problem-details")]
    [InlineData("paging-filtering")]
    [InlineData("streaming")]
    [InlineData("terminal-state")]
    [InlineData("body")]
    public void Every_http_facet_mutation_is_detected_and_only_exact_approval_passes(string facet)
    {
        var beforeObservation = Observation();
        var afterObservation = facet switch
        {
            CompatibilityFacet.Binding => beforeObservation with { Binding = "changed" },
            CompatibilityFacet.Json => beforeObservation with { Json = "{\"changed\":true}" },
            CompatibilityFacet.Status => beforeObservation with { StatusCode = 201 },
            CompatibilityFacet.MediaTypes => beforeObservation with { ContentType = "application/problem+json" },
            CompatibilityFacet.Headers => beforeObservation with { Headers = new Dictionary<string, string> { ["etag"] = "changed" } },
            CompatibilityFacet.ProblemDetails => beforeObservation with { ProblemDetails = "{\"status\":422}" },
            CompatibilityFacet.PagingFiltering => beforeObservation with { PagingFiltering = "page=2" },
            CompatibilityFacet.Streaming => beforeObservation with { Streaming = "changed" },
            CompatibilityFacet.TerminalState => beforeObservation with { TerminalState = "Cancelled" },
            _ => beforeObservation with { Body = "changed" }
        };
        var before = new CompatibilityEvidenceSet { Http = [beforeObservation] };
        var after = new CompatibilityEvidenceSet { Http = [afterObservation] };

        var failure = CompatibilityComparer.Compare(before, after);
        Assert.False(failure.IsCompatible);
        var delta = Assert.Single(failure.Deltas);
        var approval = Approval(delta);

        Assert.True(CompatibilityComparer.Compare(before, after, [approval]).IsCompatible);
        var wrongFacet = facet == CompatibilityFacet.Body ? CompatibilityFacet.Json : CompatibilityFacet.Body;
        Assert.False(CompatibilityComparer.Compare(before, after, [approval with { Facet = wrongFacet }]).IsCompatible);
    }

    [Fact]
    public void OpenApi_mutation_requires_exact_approval_and_unused_approvals_fail()
    {
        var before = new CompatibilityEvidenceSet { OpenApi = OpenApiEvidenceCapture.Capture("{\"paths\":{\"/orders\":{\"get\":{\"responses\":{\"200\":{}}}}}}") };
        var after = new CompatibilityEvidenceSet { OpenApi = OpenApiEvidenceCapture.Capture("{\"paths\":{\"/orders\":{\"get\":{\"responses\":{\"201\":{}}}}}}") };
        var delta = Assert.Single(CompatibilityComparer.Compare(before, after).Deltas);

        Assert.True(CompatibilityComparer.Compare(before, after, [Approval(delta)]).IsCompatible);
        Assert.False(CompatibilityComparer.Compare(before, after, [Approval(delta) with { Expected = "wrong" }]).IsCompatible);
    }

    [Fact]
    public void Route_only_and_method_only_changes_report_the_matching_identity_facet()
    {
        var routeBefore = new CompatibilityEvidenceSet { Http = [Observation()] };
        var routeAfter = new CompatibilityEvidenceSet
        {
            Http = [Observation() with { Endpoint = new EndpointIdentity("/invoices", "GET") }]
        };
        var methodAfter = new CompatibilityEvidenceSet
        {
            Http = [Observation() with { Endpoint = new EndpointIdentity("/orders", "POST") }]
        };

        var routeDelta = Assert.Single(CompatibilityComparer.Compare(routeBefore, routeAfter).Deltas);
        var methodDelta = Assert.Single(CompatibilityComparer.Compare(routeBefore, methodAfter).Deltas);

        Assert.Equal(CompatibilityFacet.Route, routeDelta.Facet);
        Assert.Equal("/orders", routeDelta.Expected);
        Assert.Equal("/invoices", routeDelta.Actual);
        Assert.Equal(CompatibilityFacet.Method, methodDelta.Facet);
        Assert.Equal("GET", methodDelta.Expected);
        Assert.Equal("POST", methodDelta.Actual);
    }

    [Fact]
    public void Approval_identity_cannot_collide_when_values_contain_delimiters()
    {
        var delta = new CompatibilityDelta
        {
            Endpoint = "/orders", Method = "GET", Case = "default", Facet = CompatibilityFacet.Body,
            Expected = "a|b", Actual = "c"
        };
        var other = delta with { Expected = "a", Actual = "b|c" };

        Assert.NotEqual(delta.Key, other.Key);
    }

    private static HttpCompatibilityObservation Observation() => new()
    {
        Endpoint = new EndpointIdentity("/orders", "GET"), Case = "default", Binding = "route",
        Json = "{\"id\":1}", StatusCode = 200, ProblemDetails = "", PagingFiltering = "page=1",
        Streaming = "one", Body = "{\"id\":1}"
    };

    private static ApprovedDifference Approval(CompatibilityDelta delta) => new()
    {
        Endpoint = delta.Endpoint, Method = delta.Method, Case = delta.Case, Facet = delta.Facet,
        Expected = delta.Expected, Actual = delta.Actual, Owner = "Tests", Reason = "reviewed", FollowUp = "#1"
    };
}

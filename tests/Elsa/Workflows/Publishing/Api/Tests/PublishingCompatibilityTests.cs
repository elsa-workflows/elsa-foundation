using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Mutation bites for every consumed Publishing HTTP and OpenAPI compatibility facet.</summary>
public sealed class PublishingCompatibilityTests
{
    [Fact]
    public void Http_fixture_mutations_are_detected_per_consumed_facet()
    {
        var baseline = Evidence();
        var observation = baseline.Http.First(item => item.Case == "WorkflowPublish|trusted-success");
        var replacements = new (string Facet, HttpCompatibilityObservation Observation)[]
        {
            (CompatibilityFacet.Binding, observation with { Binding = observation.Binding + "<mutation>" }),
            (CompatibilityFacet.Json, observation with { Json = observation.Json + "<mutation>" }),
            (CompatibilityFacet.Status, observation with { StatusCode = 299 }),
            (CompatibilityFacet.MediaTypes, observation with { ContentType = "application/mutated" }),
            (CompatibilityFacet.Headers, observation with { Headers = new Dictionary<string, string> { ["x-mutation"] = "true" } }),
            (CompatibilityFacet.ProblemDetails, observation with { ProblemDetails = "<mutation>" }),
            (CompatibilityFacet.PagingFiltering, observation with { PagingFiltering = "<mutation>" }),
            (CompatibilityFacet.Streaming, observation with { Streaming = "<mutation>" }),
            (CompatibilityFacet.TerminalState, observation with { TerminalState = "Mutated" }),
            (CompatibilityFacet.Body, observation with { Body = observation.Body + "<mutation>" })
        };

        foreach (var (facet, replacement) in replacements)
        {
            var after = baseline with { Http = Replace(baseline.Http, observation, replacement) };
            var delta = Assert.Single(CompatibilityComparer.Compare(baseline, after).Deltas);
            Assert.Equal(facet, delta.Facet);
        }
    }

    [Fact]
    public void Route_and_method_mutations_are_detected()
    {
        var baseline = Evidence();
        var observation = baseline.Http.First(item => item.Case == "WorkflowPublish|trusted-success");
        var routeMutation = observation with { Endpoint = new EndpointIdentity("/publishing/workflows/{versionId}/mutated", "POST") };
        var methodMutation = observation with { Endpoint = new EndpointIdentity(observation.Endpoint.Route.Value, "PATCH") };

        Assert.Contains(
            CompatibilityComparer.Compare(baseline, baseline with { Http = Replace(baseline.Http, observation, routeMutation) }).Deltas,
            delta => delta.Facet == CompatibilityFacet.Route);
        Assert.Contains(
            CompatibilityComparer.Compare(baseline, baseline with { Http = Replace(baseline.Http, observation, methodMutation) }).Deltas,
            delta => delta.Facet == CompatibilityFacet.Method);
    }

    [Fact]
    public void Every_openapi_identity_and_contract_surface_mutation_is_detected()
    {
        var baseline = Evidence();
        var document = baseline.OpenApi!;
        var operation = document.Operations.First();
        var replacements = new OpenApiOperationEvidence[]
        {
            operation with { Endpoint = new EndpointIdentity(operation.Endpoint.Route.Value + "/mutated", operation.Endpoint.Method.Value) },
            operation with { OperationId = operation.OperationId + "Mutated" },
            operation with { Tags = operation.Tags + "<mutation>" },
            operation with { Security = operation.Security + "<mutation>" },
            operation with { Parameters = operation.Parameters + "<mutation>" },
            operation with { RequestBody = operation.RequestBody + "<mutation>" },
            operation with { Responses = operation.Responses + "<mutation>" },
            operation with { MediaTypes = operation.MediaTypes + "<mutation>" },
            operation with { Schemas = operation.Schemas + "<mutation>" }
        };

        foreach (var replacement in replacements)
        {
            var afterDocument = new OpenApiEvidenceDocument(
                document.Operations.Select(item => item == operation ? replacement : item).ToArray());
            var deltas = CompatibilityComparer.Compare(
                baseline,
                baseline with { OpenApi = afterDocument }).Deltas;
            Assert.NotEmpty(deltas);
            Assert.All(deltas, delta => Assert.Equal(CompatibilityFacet.OpenApi, delta.Facet));
        }
    }

    [Fact]
    public void Empty_initial_approval_registry_cannot_hide_any_mutation()
    {
        var baseline = Evidence();
        var approvals = BaselineFile.Load<ApprovedDifference[]>(Path.Join(BaselineDirectory, "publishing-approved-differences.initial.json"));
        var observation = baseline.Http[0];
        var mutated = baseline with
        {
            Http = Replace(baseline.Http, observation, observation with { StatusCode = 599 })
        };

        Assert.Empty(approvals);
        var comparison = CompatibilityComparer.CompareBidirectional(baseline, mutated, approvals);
        Assert.False(comparison.IsCompatible);
        Assert.Equal(2, comparison.Deltas.Count);
        Assert.Empty(comparison.InvalidApprovals);
    }

    private static CompatibilityEvidenceSet Evidence() => new()
    {
        Http = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory, "publishing-http-fastendpoints.json")),
        OpenApi = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "publishing-openapi-fastendpoints.json"))
    };

    private static IReadOnlyList<HttpCompatibilityObservation> Replace(
        IReadOnlyList<HttpCompatibilityObservation> source,
        HttpCompatibilityObservation oldValue,
        HttpCompatibilityObservation newValue) =>
        source.Select(item => item == oldValue ? newValue : item).ToArray();

    private static string BaselineDirectory => Path.Join(AppContext.BaseDirectory, "Baselines");
}

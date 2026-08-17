using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

public sealed class ActivitiesDesignCompatibilityTests
{
    [Theory]
    [InlineData(CompatibilityFacet.Route)]
    [InlineData(CompatibilityFacet.Method)]
    [InlineData(CompatibilityFacet.Binding)]
    [InlineData(CompatibilityFacet.Json)]
    [InlineData(CompatibilityFacet.Status)]
    [InlineData(CompatibilityFacet.MediaTypes)]
    [InlineData(CompatibilityFacet.Headers)]
    [InlineData(CompatibilityFacet.ProblemDetails)]
    [InlineData(CompatibilityFacet.PagingFiltering)]
    [InlineData(CompatibilityFacet.Streaming)]
    [InlineData(CompatibilityFacet.TerminalState)]
    [InlineData(CompatibilityFacet.Body)]
    public void Frozen_http_oracle_detects_every_consumed_facet_mutation(string facet)
    {
        var before = LoadHttp();
        var candidate = before[0];
        var mutated = facet switch
        {
            CompatibilityFacet.Route => candidate with { Endpoint = new EndpointIdentity("/mutated", candidate.Endpoint.Method.Value) },
            CompatibilityFacet.Method => candidate with { Endpoint = new EndpointIdentity(candidate.Endpoint.Route.Value, "OPTIONS") },
            CompatibilityFacet.Binding => candidate with { Binding = "mutated" },
            CompatibilityFacet.Json => candidate with { Json = "{\"mutated\":true}" },
            CompatibilityFacet.Status => candidate with { StatusCode = 599 },
            CompatibilityFacet.MediaTypes => candidate with { ContentType = "application/mutated" },
            CompatibilityFacet.Headers => candidate with { Headers = new Dictionary<string, string> { ["x-mutated"] = "true" } },
            CompatibilityFacet.ProblemDetails => candidate with { ProblemDetails = "{\"mutated\":true}" },
            CompatibilityFacet.PagingFiltering => candidate with { PagingFiltering = "mutated" },
            CompatibilityFacet.Streaming => candidate with { Streaming = "mutated" },
            CompatibilityFacet.TerminalState => candidate with { TerminalState = "Mutated" },
            _ => candidate with { Body = "mutated" }
        };
        var after = before.Select((observation, index) => index == 0 ? mutated : observation).ToArray();

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = after });

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Deltas, delta => delta.Facet == facet);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("method")]
    [InlineData("operationId")]
    [InlineData("tags")]
    [InlineData("security")]
    [InlineData("parameters")]
    [InlineData("requestBody")]
    [InlineData("responses")]
    [InlineData("mediaTypes")]
    [InlineData("schemas")]
    public void Frozen_openapi_oracle_detects_every_consumed_operation_mutation(string facet)
    {
        var before = LoadOpenApi();
        var candidate = before.Operations[0];
        var mutated = facet switch
        {
            "route" => candidate with { Endpoint = new EndpointIdentity("/mutated", candidate.Endpoint.Method.Value) },
            "method" => candidate with { Endpoint = new EndpointIdentity(candidate.Endpoint.Route.Value, "OPTIONS") },
            "operationId" => candidate with { OperationId = "mutated" },
            "tags" => candidate with { Tags = "[\"mutated\"]" },
            "security" => candidate with { Security = "[{\"mutated\":[]}]" },
            "parameters" => candidate with { Parameters = "[\"mutated\"]" },
            "requestBody" => candidate with { RequestBody = "{\"mutated\":true}" },
            "responses" => candidate with { Responses = "{\"599\":{}}" },
            "mediaTypes" => candidate with { MediaTypes = "[\"application/mutated\"]" },
            _ => candidate with { Schemas = "{\"mutated\":true}" }
        };
        var after = new OpenApiEvidenceDocument(before.Operations.Select((operation, index) => index == 0 ? mutated : operation).ToArray());

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { OpenApi = before },
            new CompatibilityEvidenceSet { OpenApi = after });

        Assert.False(result.IsCompatible);
        Assert.True(result.Deltas.Count > 0);
    }

    private static HttpCompatibilityObservation[] LoadHttp() =>
        BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory(), "activities-design-http-fastendpoints.json"));

    private static OpenApiEvidenceDocument LoadOpenApi() =>
        BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory(), "activities-design-openapi-fastendpoints.json"));

    private static string BaselineDirectory() => Path.Join(AppContext.BaseDirectory, "Baselines");
}

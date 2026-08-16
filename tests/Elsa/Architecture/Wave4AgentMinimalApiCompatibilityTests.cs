using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentMinimalApiCompatibilityTests
{
    private static readonly string BaselineDirectory = Path.Join(AppContext.BaseDirectory, "Baselines");

    [Fact]
    public async Task Minimal_api_after_evidence_matches_the_frozen_agent_contract()
    {
        var beforeHttp = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory, "wave4-agent-http-fastendpoints.json"));
        var beforeOpenApi = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave4-agent-openapi-fastendpoints.json"));

        await using var host = await Wave4AgentMinimalApiHost.StartAsync();
        var afterHttp = (await Task.WhenAll(Wave4AgentFastEndpointsBaselineTests.Cases.Select(testCase =>
            HttpEvidenceCapture.CaptureAsync(host.Client, testCase)))).ToArray();
        var afterOpenApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());
        afterOpenApi = new OpenApiEvidenceDocument(afterOpenApi.Operations
            .Where(operation => operation.Endpoint.Route.Value.StartsWith("/_elsa/agent", StringComparison.Ordinal))
            .ToArray());

        Assert.Equal(11, afterHttp.Select(item => item.Endpoint).Distinct().Count());
        Assert.Equal(11, afterOpenApi.Operations.Count);

        var comparison = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = beforeHttp, OpenApi = beforeOpenApi },
            new CompatibilityEvidenceSet { Http = afterHttp, OpenApi = afterOpenApi },
            []);

        Assert.True(comparison.IsCompatible, string.Join(Environment.NewLine, comparison.Failures));
    }
}

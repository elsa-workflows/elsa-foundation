using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;

namespace Elsa.Secrets.Tests.Support;

internal static class SecretsCompatibilityEvidence
{
    private static readonly string HttpBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "secrets-http-fastendpoints.json");
    private static readonly string OpenApiBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "secrets-openapi-fastendpoints.json");

    public static HttpCompatibilityObservation[] LoadLegacyHttp(IReadOnlySet<string> endpoints) =>
        BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath)
            .Where(observation => endpoints.Contains(observation.Endpoint.ToString()))
            .ToArray();

    public static OpenApiEvidenceDocument LoadLegacyOpenApi(IReadOnlySet<string> endpoints)
    {
        var baseline = BaselineFile.Load<OpenApiEvidenceDocument>(OpenApiBaselinePath);
        return Filter(baseline, endpoints);
    }

    public static OpenApiEvidenceDocument CaptureOpenApi(string document, IReadOnlySet<string> endpoints) =>
        Filter(OpenApiEvidenceCapture.Capture(document), endpoints);

    public static HttpCompatibilityCase[] Cases(IReadOnlySet<string> endpoints) =>
        SecretsCompatibilityCases.All.Where(testCase => endpoints.Contains(testCase.Endpoint.ToString())).ToArray();

    private static OpenApiEvidenceDocument Filter(OpenApiEvidenceDocument document, IReadOnlySet<string> endpoints) =>
        new(document.Operations.Where(operation => endpoints.Contains(operation.Endpoint.ToString())).ToArray());
}

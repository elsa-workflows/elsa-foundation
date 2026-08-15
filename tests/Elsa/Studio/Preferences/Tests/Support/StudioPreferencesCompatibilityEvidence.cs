using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;

namespace Elsa.Studio.Preferences.Tests.Support;

internal static class StudioPreferencesCompatibilityEvidence
{
    private static readonly string HttpBaselinePath = Path.Combine(
        AppContext.BaseDirectory, "Baselines", "studio-preferences-http-fastendpoints.json");
    private static readonly string OpenApiBaselinePath = Path.Combine(
        AppContext.BaseDirectory, "Baselines", "studio-preferences-openapi-fastendpoints.json");
    private static readonly string ApprovalsPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../../Architecture/Baselines/rest-compatibility-approved-differences.json"));

    public static HttpCompatibilityObservation[] LoadLegacyHttp(string method) =>
        BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath)
            .Where(observation => observation.Endpoint.Method.Value == method)
            .ToArray();

    public static OpenApiEvidenceDocument LoadLegacyOpenApi(string method)
    {
        var baseline = BaselineFile.Load<OpenApiEvidenceDocument>(OpenApiBaselinePath);
        return new OpenApiEvidenceDocument(baseline.Operations
            .Where(operation => operation.Endpoint.Method.Value == method)
            .ToArray());
    }

    public static ApprovedDifference[] LoadApprovals() =>
        BaselineFile.Load<ApprovedDifference[]>(ApprovalsPath);
}

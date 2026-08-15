using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;

namespace Elsa.Studio.Preferences.Tests.Support;

internal static class StudioPreferencesCompatibilityEvidence
{
    private static readonly string HttpBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "studio-preferences-http-fastendpoints.json");
    private static readonly string OpenApiBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "studio-preferences-openapi-fastendpoints.json");
    private static readonly string ApprovalsPath = Path.GetFullPath(
        "../../../../../../Architecture/Baselines/rest-compatibility-approved-differences.json",
        AppContext.BaseDirectory);

    public static HttpCompatibilityObservation[] LoadLegacyHttp(string method) => NormalizeVolatileFields(
        BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath)
            .Where(observation => observation.Endpoint.Method.Value == method));

    public static HttpCompatibilityObservation[] NormalizeVolatileFields(
        IEnumerable<HttpCompatibilityObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return observations.Select(observation => observation with
        {
            Json = NormalizeTraceIdentifier(observation.Json),
            Body = NormalizeTraceIdentifier(observation.Body),
            ProblemDetails = NormalizeTraceIdentifier(observation.ProblemDetails)
        }).ToArray();
    }

    public static OpenApiEvidenceDocument LoadLegacyOpenApi(string method)
    {
        var baseline = BaselineFile.Load<OpenApiEvidenceDocument>(OpenApiBaselinePath);
        return new OpenApiEvidenceDocument(baseline.Operations
            .Where(operation => operation.Endpoint.Method.Value == method)
            .ToArray());
    }

    public static ApprovedDifference[] LoadApprovals() =>
        BaselineFile.Load<ApprovedDifference[]>(ApprovalsPath);

    private static string NormalizeTraceIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return value;
        }

        if (node is not JsonObject json || !json.ContainsKey("traceId"))
            return value;

        json["traceId"] = "<trace-id>";
        return CompatibilityJson.Canonicalize(json);
    }
}

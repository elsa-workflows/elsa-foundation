using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;

namespace Elsa.Workflows.Design.Api.Tests.Support;

internal static class WorkflowDesignCompatibilityEvidence
{
    public const string HttpFileName = "workflows-design-http-fastendpoints.json";
    public const string OpenApiFileName = "workflows-design-openapi-fastendpoints.json";
    public const string HandlerTraceFileName = "workflows-design-handler-trace-fastendpoints.json";
    public const string ProvenanceFileName = "workflows-design-before-provenance.json";
    public const string ReceiptFileName = "workflows-design-before-capture-receipt.json";

    private static readonly string HttpBaselinePath = Path.Join(AppContext.BaseDirectory, "Baselines", HttpFileName);
    private static readonly string OpenApiBaselinePath = Path.Join(AppContext.BaseDirectory, "Baselines", OpenApiFileName);
    private static readonly string HandlerTraceBaselinePath = Path.Join(AppContext.BaseDirectory, "Baselines", HandlerTraceFileName);

    public static IReadOnlyList<HttpCompatibilityObservation> LoadLegacyHttp() =>
        BaselineFile.Load<HttpCompatibilityObservation[]>(HttpBaselinePath);

    public static OpenApiEvidenceDocument LoadLegacyOpenApi() =>
        BaselineFile.Load<OpenApiEvidenceDocument>(OpenApiBaselinePath);

    public static string ReadHttpBaseline() => BaselineFile.Read(HttpBaselinePath);
    public static string ReadOpenApiBaseline() => BaselineFile.Read(OpenApiBaselinePath);
    public static string ReadHandlerTraceBaseline() => BaselineFile.Read(HandlerTraceBaselinePath);

    /// <summary>
    /// Reviewed divergences from the frozen FastEndpoints evidence. Each entry is applied in both
    /// comparison directions, and the comparer rejects any entry that no longer matches a real
    /// delta, so this file cannot silently rot.
    /// </summary>
    public static ApprovedDifference[] LoadApprovals()
    {
        var path = Path.Join(AppContext.BaseDirectory, "Baselines", "workflows-design-approved-differences.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray()
            .SelectMany(entry =>
            {
                var approval = new ApprovedDifference
                {
                    Endpoint = entry.GetProperty("endpoint").GetString()!,
                    Method = entry.GetProperty("method").GetString()!,
                    Case = entry.GetProperty("case").GetString()!,
                    Facet = entry.GetProperty("facet").GetString()!,
                    Expected = entry.GetProperty("expected").GetString()!,
                    Actual = entry.GetProperty("actual").GetString()!,
                    Owner = entry.GetProperty("owner").GetString()!,
                    Reason = entry.GetProperty("reason").GetString()!,
                    FollowUp = entry.GetProperty("followUp").GetString()!
                };
                return new[] { approval, approval with { Reverse = true } };
            })
            .ToArray();
    }

}

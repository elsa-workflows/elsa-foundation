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

    public static ApprovedDifference[] LoadApprovals() => [];

}

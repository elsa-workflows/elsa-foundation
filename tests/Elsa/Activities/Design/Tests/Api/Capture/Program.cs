using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Activities.Design.Tests.Api.Support;

var sourceRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var outputDirectory = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(sourceRoot, "tests/Elsa/Activities/Design/Tests/Api/Baselines");
var sourceCommit = Environment.GetEnvironmentVariable("ACTIVITIES_DESIGN_BEFORE_COMMIT")
                   ?? throw new InvalidOperationException("ACTIVITIES_DESIGN_BEFORE_COMMIT must pin the historical source commit.");
var runnerIdentity = Environment.GetEnvironmentVariable("ACTIVITIES_DESIGN_CAPTURE_RUNNER_IDENTITY") ?? "checked-in-commit";

Directory.CreateDirectory(outputDirectory);
await using var host = await ActivitiesDesignCompatibilityHost.StartAsync();
var observations = new List<HttpCompatibilityObservation>(ActivitiesDesignCompatibilityCases.All.Count);
foreach (var testCase in ActivitiesDesignCompatibilityCases.All)
    observations.Add(NormalizeVolatileFields(await CaptureAsync(host.Client, testCase)));

var rawOpenApi = await host.GetOpenApiAsync();
var projectedOpenApi = OpenApiEvidenceCapture.Capture(rawOpenApi, includeIdentityMetadata: true);
if (projectedOpenApi.Operations.Count(operation => operation.Endpoint.Route.Value.StartsWith("/design/activities", StringComparison.Ordinal)) != 38)
    throw new InvalidDataException("The before capture did not consume exactly 38 Activities Design OpenAPI operations.");

var approvals = "[]";
var httpPath = Path.Join(outputDirectory, "activities-design-http-fastendpoints.json");
var openApiPath = Path.Join(outputDirectory, "activities-design-openapi-fastendpoints.json");
var rawOpenApiPath = Path.Join(outputDirectory, "activities-design-openapi-fastendpoints.raw.json");
var approvalsPath = Path.Join(outputDirectory, "activities-design-approved-differences.json");
File.WriteAllText(httpPath, CompatibilityJson.Serialize(observations));
File.WriteAllText(openApiPath, CompatibilityJson.Serialize(projectedOpenApi));
File.WriteAllText(rawOpenApiPath, CompatibilityJson.Canonicalize(rawOpenApi));
File.WriteAllText(approvalsPath, approvals);

var dependencies = RunnerDependencies(sourceRoot, sourceCommit, runnerIdentity);
var receipt = new
{
    capture = "real-fastendpoints-historical-worktree",
    captureDescription = "git worktree add --detach sourceCommit; execute only checked-in runner content",
    sourceCommit,
    sourceRelationship = "ancestor-before-migration",
    runnerIdentity,
    captureCommand = $"ACTIVITIES_DESIGN_BEFORE_COMMIT={sourceCommit} bash tools/capture-activities-design-before.sh",
    registrationCount = 38,
    caseCount = observations.Count,
    operationCount = 38,
    runnerFingerprint = Fingerprint(dependencies),
    runnerDependencies = dependencies,
    httpSha256 = Hash(httpPath),
    openApiSha256 = Hash(openApiPath),
    rawOpenApiSha256 = Hash(rawOpenApiPath),
    approvalsSha256 = Hash(approvalsPath),
    categories = new[] { "anonymous", "trusted-success", "historical-defect", "binding", "domain", "cancellation" },
    volatileFields = new[] { "response-json.traceId" }
};
File.WriteAllText(Path.Join(outputDirectory, "activities-design-before-capture-receipt.json"), CompatibilityJson.Serialize(receipt));

static async Task<HttpCompatibilityObservation> CaptureAsync(HttpClient client, HttpCompatibilityCase testCase)
{
    try
    {
        return await HttpEvidenceCapture.CaptureAsync(client, testCase);
    }
    catch (Exception exception)
    {
        var terminal = exception.GetBaseException();
        return new HttpCompatibilityObservation
        {
            Endpoint = testCase.Endpoint,
            Case = testCase.Case,
            Binding = testCase.Binding ?? "",
            PagingFiltering = testCase.PagingFiltering ?? "",
            StatusCode = 0,
            TerminalState = $"Faulted:{terminal.GetType().FullName}"
        };
    }
}

static HttpCompatibilityObservation NormalizeVolatileFields(HttpCompatibilityObservation observation)
{
    if (observation.Json.Length == 0)
        return observation;

    var node = JsonNode.Parse(observation.Json);
    if (node is not JsonObject body || !body.ContainsKey("traceId"))
        return observation;

    body["traceId"] = "<volatile-trace-id>";
    var normalized = CompatibilityJson.Canonicalize(body.ToJsonString());
    return observation with
    {
        Json = normalized,
        Body = observation.Body == observation.Json ? normalized : observation.Body,
        ProblemDetails = observation.ProblemDetails == observation.Json ? normalized : observation.ProblemDetails
    };
}

static IReadOnlyList<RunnerDependency> RunnerDependencies(string sourceRoot, string sourceCommit, string runnerIdentity) =>
new[]
{
    "tools/capture-activities-design-before.sh",
    "tests/Elsa/Activities/Design/Tests/Api/Capture/Elsa.Activities.Design.BeforeCapture.csproj",
    "tests/Elsa/Activities/Design/Tests/Api/Capture/Program.cs",
    "tests/Elsa/Activities/Design/Tests/Api/Support/ActivitiesDesignCompatibilityCases.cs",
    "tests/Elsa/Activities/Design/Tests/Api/Support/ActivitiesDesignCompatibilityHost.cs",
    "tests/Elsa/Api/Compatibility/Testing/Http/HttpEvidenceCapture.cs",
    "tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs",
    "tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs",
    "tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs"
}.Select(path => new RunnerDependency(
    path.StartsWith("tests/Elsa/Api/", StringComparison.Ordinal)
        ? $"source-commit:{sourceCommit}"
        : runnerIdentity,
    path,
    Hash(Path.Combine(sourceRoot, path)))).ToArray();

static string Fingerprint(IEnumerable<RunnerDependency> dependencies) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", dependencies.Select(dependency => $"{dependency.Identity}|{dependency.Path}|{dependency.Sha256}")) + "\n"))).ToLowerInvariant();

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

internal sealed record RunnerDependency(string Identity, string Path, string Sha256);

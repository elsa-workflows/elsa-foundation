using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

var sourceRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var outputDirectory = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(sourceRoot, "tests/Elsa/Workflows/Publishing/Api/Tests/Baselines");
var sourceCommit = Environment.GetEnvironmentVariable("PUBLISHING_BEFORE_COMMIT")
                   ?? throw new InvalidOperationException("PUBLISHING_BEFORE_COMMIT must pin the historical source commit.");
var runnerIdentity = Environment.GetEnvironmentVariable("PUBLISHING_CAPTURE_RUNNER_IDENTITY") ?? "checked-in-commit";

Directory.CreateDirectory(outputDirectory);
await using var host = await PublishingCompatibilityHost.StartAsync();
var observations = new List<HttpCompatibilityObservation>(PublishingCompatibilityCases.All.Count);
foreach (var testCase in PublishingCompatibilityCases.All)
    observations.Add(NormalizeVolatileFields(await CaptureAsync(host.Client, testCase)));

var rawOpenApi = await host.GetOpenApiAsync();
var projectedOpenApi = OpenApiEvidenceCapture.Capture(rawOpenApi, includeIdentityMetadata: true);
var publishingOperations = projectedOpenApi.Operations
    .Where(operation =>
        operation.Endpoint.Route.Value.StartsWith("/publishing/", StringComparison.Ordinal) ||
        operation.Endpoint.Route.Value.StartsWith("/design/activities/", StringComparison.Ordinal))
    .ToArray();
if (publishingOperations.Length != 23)
    throw new InvalidDataException($"The before capture consumed {publishingOperations.Length} Publishing operations instead of 23.");

var evidence = new OpenApiEvidenceDocument(publishingOperations);
var approvals = "[]\n";
var httpPath = Path.Join(outputDirectory, "publishing-http-fastendpoints.json");
var openApiPath = Path.Join(outputDirectory, "publishing-openapi-fastendpoints.json");
var rawOpenApiPath = Path.Join(outputDirectory, "publishing-openapi-fastendpoints.raw.json");
var approvalsPath = Path.Join(outputDirectory, "publishing-approved-differences.initial.json");
File.WriteAllText(httpPath, CompatibilityJson.Serialize(observations));
File.WriteAllText(openApiPath, CompatibilityJson.Serialize(evidence));
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
    captureCommand = $"PUBLISHING_BEFORE_COMMIT={sourceCommit} bash tools/capture-publishing-before.sh",
    registrationCount = 23,
    caseCount = observations.Count,
    operationCount = publishingOperations.Length,
    runnerFingerprint = Fingerprint(dependencies),
    runnerDependencies = dependencies,
    httpSha256 = Hash(httpPath),
    openApiSha256 = Hash(openApiPath),
    rawOpenApiSha256 = Hash(rawOpenApiPath),
    initialApprovalsSha256 = Hash(approvalsPath),
    categories = new[] { "anonymous", "trusted-success", "binding", "domain", "cancellation" },
    volatileFields = new[] { "response-json.traceId", "response-json.preflightToken", "response-header.date" }
};
File.WriteAllText(
    Path.Join(outputDirectory, "publishing-before-capture-receipt.json"),
    CompatibilityJson.Serialize(receipt) + Environment.NewLine);

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
    var headers = observation.Headers
        .Where(header => !string.Equals(header.Key, "date", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(header => header.Key, header => header.Value, StringComparer.Ordinal);
    if (observation.Json.Length == 0)
        return observation with { Headers = headers };

    var node = JsonNode.Parse(observation.Json);
    if (node is not JsonObject body)
        return observation with { Headers = headers };

    var changed = false;
    if (body.ContainsKey("traceId"))
    {
        body["traceId"] = "<volatile-trace-id>";
        changed = true;
    }

    if (body.ContainsKey("preflightToken"))
    {
        body["preflightToken"] = "<volatile-preflight-token>";
        changed = true;
    }

    if (!changed)
        return observation with { Headers = headers };

    var normalized = CompatibilityJson.Canonicalize(body.ToJsonString());
    return observation with
    {
        Headers = headers,
        Json = normalized,
        Body = observation.Body == observation.Json ? normalized : observation.Body,
        ProblemDetails = observation.ProblemDetails == observation.Json ? normalized : observation.ProblemDetails
    };
}

static IReadOnlyList<RunnerDependency> RunnerDependencies(string sourceRoot, string sourceCommit, string runnerIdentity) =>
new[]
{
    "tools/capture-publishing-before.sh",
    "tests/Elsa/Workflows/Publishing/Api/Tests/Capture/Elsa.Workflows.Publishing.BeforeCapture.csproj",
    "tests/Elsa/Workflows/Publishing/Api/Tests/Capture/Program.cs",
    "tests/Elsa/Workflows/Publishing/Api/Tests/Support/PublishingCompatibilityCases.cs",
    "tests/Elsa/Workflows/Publishing/Api/Tests/Support/PublishingCompatibilityHost.cs",
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

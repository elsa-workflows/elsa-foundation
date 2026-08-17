using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Tests.Api.Support;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Serialization;

var sourceRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var outputDirectory = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(sourceRoot, "tests/Elsa/Activities/Design/Tests/Api/Baselines");
var sourceCommit = Environment.GetEnvironmentVariable("ACTIVITIES_DESIGN_BEFORE_COMMIT")
                   ?? throw new InvalidOperationException("ACTIVITIES_DESIGN_BEFORE_COMMIT must pin the historical source commit.");

Directory.CreateDirectory(outputDirectory);
await using var host = await ActivitiesDesignCompatibilityHost.StartAsync();
var observations = new List<HttpCompatibilityObservation>(ActivitiesDesignQueryBindingCases.All.Count);
foreach (var testCase in ActivitiesDesignQueryBindingCases.All)
    observations.Add(NormalizeVolatileFields(await HttpEvidenceCapture.CaptureAsync(host.Client, testCase)));

var evidencePath = Path.Join(outputDirectory, "activities-design-query-binding-fastendpoints.json");
File.WriteAllText(evidencePath, CompatibilityJson.Serialize(observations));

var dependencies = RunnerDependencies(sourceRoot, sourceCommit);
var receipt = new
{
    capture = "real-fastendpoints-query-binding-supplement",
    captureDescription = "git worktree add --detach sourceCommit; overlay and execute only checked-in supplement runner content",
    sourceCommit,
    sourceRelationship = "ancestor-before-migration",
    runnerIdentity = "checked-in-commit",
    captureCommand = $"ACTIVITIES_DESIGN_BEFORE_COMMIT={sourceCommit} bash tools/capture-activities-design-query-binding-before.sh",
    caseCount = observations.Count,
    runnerFingerprint = Fingerprint(dependencies),
    runnerDependencies = dependencies,
    evidenceSha256 = Hash(evidencePath),
    volatileFields = new[] { "response-json.traceId" }
};
File.WriteAllText(
    Path.Join(outputDirectory, "activities-design-query-binding-before-receipt.json"),
    CompatibilityJson.Serialize(receipt) + Environment.NewLine);

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

static IReadOnlyList<RunnerDependency> RunnerDependencies(string sourceRoot, string sourceCommit) =>
new[]
{
    "tools/capture-activities-design-query-binding-before.sh",
    "tests/Elsa/Activities/Design/Tests/Api/Capture/QueryBinding/Elsa.Activities.Design.QueryBinding.BeforeCapture.csproj",
    "tests/Elsa/Activities/Design/Tests/Api/Capture/QueryBinding/Program.cs",
    "tests/Elsa/Activities/Design/Tests/Api/Capture/QueryBinding/ActivitiesDesignQueryBindingCases.cs",
    "tests/Elsa/Activities/Design/Tests/Api/Capture/Frozen/ActivitiesDesignCompatibilityCases.cs",
    "tests/Elsa/Activities/Design/Tests/Api/Capture/Frozen/ActivitiesDesignCompatibilityHost.cs",
    "tests/Elsa/Api/Compatibility/Testing/Http/HttpEvidenceCapture.cs",
    "tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs",
    "tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs"
}.Select(path => new RunnerDependency(
    path.StartsWith("tests/Elsa/Api/", StringComparison.Ordinal)
        ? $"source-commit:{sourceCommit}"
        : "checked-in-commit",
    path,
    Hash(Path.Combine(sourceRoot, path)))).ToArray();

static string Fingerprint(IEnumerable<RunnerDependency> dependencies) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", dependencies.Select(dependency => $"{dependency.Identity}|{dependency.Path}|{dependency.Sha256}")) + "\n"))).ToLowerInvariant();

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

internal sealed record RunnerDependency(string Identity, string Path, string Sha256);

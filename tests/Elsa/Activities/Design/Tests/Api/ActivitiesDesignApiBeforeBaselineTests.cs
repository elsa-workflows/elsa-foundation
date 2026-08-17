using Elsa.Activities.Design.Tests.Api.Support;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

/// <summary>Immutable-before gates for the real FastEndpoints owner.</summary>
public sealed class ActivitiesDesignApiBeforeBaselineTests
{
    private const string HttpFileName = "activities-design-http-fastendpoints.json";
    private const string OpenApiFileName = "activities-design-openapi-fastendpoints.json";
    private const string RawOpenApiFileName = "activities-design-openapi-fastendpoints.raw.json";
    private const string ReceiptFileName = "activities-design-before-capture-receipt.json";
    private const string InitialApprovalsFileName = "activities-design-approved-differences.initial.json";

    [Fact]
    public void Reviewed_manifest_contains_exactly_38_one_to_one_route_registrations()
    {
        var routes = ActivitiesDesignCompatibilityCases.Manifest;
        Assert.Equal(38, routes.Count);
        Assert.Equal(38, routes.Select(route => route.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(38, routes.Select(route => route.Endpoint).Distinct().Count());
        Assert.Equal(38, routes.Select(route => $"{route.Endpoint.Method} {route.Endpoint.Route}").Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(7, routes.Count(route => route.SuccessStatus == 201));
        Assert.Single(routes, route => route.SuccessStatus == 204);
        Assert.All(routes, route => Assert.Contains(route.Action, new[] { "read", "manage" }));
        Assert.Equal(38, ActivitiesDesignCompatibilityCases.Anonymous.Count);
        Assert.Equal(37, ActivitiesDesignCompatibilityCases.Authenticated.Count);
        Assert.Single(ActivitiesDesignCompatibilityCases.HistoricalDefects);
        Assert.All(ActivitiesDesignCompatibilityCases.Anonymous, testCase => Assert.Contains("|anonymous", testCase.Case, StringComparison.Ordinal));
        Assert.All(ActivitiesDesignCompatibilityCases.Authenticated, testCase => Assert.Contains("|trusted-success", testCase.Case, StringComparison.Ordinal));
    }

    [Fact]
    public void Historical_fastendpoints_host_evidence_exposes_exactly_the_reviewed_38_registrations()
    {
        var source = File.ReadAllText(Path.Join(
            RepositoryRoot,
            "tests/Elsa/Activities/Design/Tests/Api/Capture/Frozen/ActivitiesDesignCompatibilityHost.cs"));
        Assert.Contains("services.AddFastEndpoints(options => options.Assemblies = [typeof(ActivitiesDesignApiFeature).Assembly])", source, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapFastEndpoints", source, StringComparison.Ordinal);

        var document = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory(), OpenApiFileName));
        var endpoints = document.Operations
            .Where(operation => operation.Endpoint.Route.Value.StartsWith("/design/activities", StringComparison.Ordinal))
            .Select(operation => operation.Endpoint)
            .ToArray();

        Assert.Equal(38, endpoints.Length);
        Assert.Equal(38, endpoints.Distinct().Count());
        Assert.Equal(
            ActivitiesDesignCompatibilityCases.Manifest.Select(route => route.Endpoint).OrderBy(endpoint => endpoint.ToString()),
            endpoints.OrderBy(endpoint => endpoint.ToString()));
    }

    [Fact]
    public void Historical_http_fixture_covers_all_38_anonymous_challenges_and_authenticated_cases()
    {
        var observations = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory(), HttpFileName));
        Assert.Equal(ActivitiesDesignCompatibilityCases.All.Count, observations.Length);
        Assert.Equal(38, observations.Count(observation => observation.Case.EndsWith("|anonymous", StringComparison.Ordinal)));
        Assert.All(observations.Where(observation => observation.Case.EndsWith("|anonymous", StringComparison.Ordinal)), observation => Assert.Equal(401, observation.StatusCode));
        Assert.Equal(37, observations.Count(observation => observation.Case.EndsWith("|trusted-success", StringComparison.Ordinal)));
        foreach (var route in ActivitiesDesignCompatibilityCases.Manifest.Where(route => route.Id != "Forks.GetStatus"))
            Assert.Contains(observations, observation => observation.Case == route.Id + "|trusted-success" && observation.StatusCode == route.SuccessStatus);
        var routeOnlyDefect = Assert.Single(observations, observation => observation.Case == "Forks.GetStatus|trusted-route-only-binding-failure");
        Assert.Equal(0, routeOnlyDefect.StatusCode);
        Assert.Equal("Faulted:System.NotSupportedException", routeOnlyDefect.TerminalState);
        Assert.Contains(observations, observation => observation.Case == "Definitions.Add|trusted-malformed-json" && observation.StatusCode is 400 or 422);
        Assert.Contains(observations, observation => observation.Case == "Availability.GetSettings|trusted-domain-not-found" && observation.StatusCode == 404);
        Assert.Contains(observations, observation => observation.Case == "Drafts.Validate|trusted-domain-conflict" && observation.StatusCode == 409);
        Assert.Contains(observations, observation => observation.Case == "UpgradePlans.Apply|trusted-domain-failure" && observation.StatusCode == 422);
        Assert.Contains(observations, observation => observation.Case == "Drafts.Get|trusted-cancellation" && observation.TerminalState.Contains("OperationCanceledException", StringComparison.Ordinal));
    }

    [Fact]
    public void Historical_openapi_fixture_consumes_exactly_38_operations()
    {
        var document = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory(), OpenApiFileName));
        var operations = document.Operations
            .Where(operation => operation.Endpoint.Route.Value.StartsWith("/design/activities", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(38, operations.Length);
        Assert.Equal(38, operations.Select(operation => operation.Endpoint).Distinct().Count());
        Assert.All(operations, operation =>
        {
            Assert.NotEmpty(operation.Responses);
            Assert.NotEmpty(operation.Schemas);
        });
        Assert.Equal(
            ActivitiesDesignCompatibilityCases.Manifest.Select(route => route.Endpoint).OrderBy(endpoint => endpoint.ToString()),
            operations.Select(operation => operation.Endpoint).OrderBy(endpoint => endpoint.ToString()));
    }

    [Fact]
    public void Before_fixture_receipt_hashes_http_projected_raw_openapi_and_initial_empty_approvals()
    {
        var directory = BaselineDirectory();
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(directory, ReceiptFileName)));
        var root = receipt.RootElement;
        Assert.Equal(38, root.GetProperty("registrationCount").GetInt32());
        Assert.Equal(38, root.GetProperty("operationCount").GetInt32());
        Assert.Equal(ActivitiesDesignCompatibilityCases.All.Count, root.GetProperty("caseCount").GetInt32());
        Assert.Equal(Hash(Path.Join(directory, HttpFileName)), root.GetProperty("httpSha256").GetString());
        Assert.Equal(Hash(Path.Join(directory, OpenApiFileName)), root.GetProperty("openApiSha256").GetString());
        Assert.Equal(Hash(Path.Join(directory, RawOpenApiFileName)), root.GetProperty("rawOpenApiSha256").GetString());
        Assert.Equal(Hash(Path.Join(directory, InitialApprovalsFileName)), root.GetProperty("initialApprovalsSha256").GetString());
        Assert.Equal("checked-in-commit", root.GetProperty("runnerIdentity").GetString());
        Assert.False(root.TryGetProperty("runnerCommit", out _));
        Assert.Contains("ACTIVITIES_DESIGN_BEFORE_COMMIT=", root.GetProperty("captureCommand").GetString(), StringComparison.Ordinal);
        Assert.Contains("git worktree add --detach", root.GetProperty("captureDescription").GetString(), StringComparison.Ordinal);
        Assert.Equal("[]", BaselineFile.LoadCanonical(Path.Join(directory, InitialApprovalsFileName)));
    }

    [Fact]
    public void Receipt_pins_branch_durable_runner_dependencies_and_clean_source_identity()
    {
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(BaselineDirectory(), ReceiptFileName)));
        var root = receipt.RootElement;
        var sourceCommit = root.GetProperty("sourceCommit").GetString();
        Assert.Matches("^[0-9a-f]{40}$", sourceCommit!);
        Assert.Equal("ancestor-before-migration", root.GetProperty("sourceRelationship").GetString());
        var dependencies = ReadDependencies(root);
        Assert.Equal(
            new[]
            {
                "tools/capture-activities-design-before.sh",
                "tests/Elsa/Activities/Design/Tests/Api/Capture/Elsa.Activities.Design.BeforeCapture.csproj",
                "tests/Elsa/Activities/Design/Tests/Api/Capture/Program.cs",
                "tests/Elsa/Activities/Design/Tests/Api/Capture/Frozen/ActivitiesDesignCompatibilityCases.cs",
                "tests/Elsa/Activities/Design/Tests/Api/Capture/Frozen/ActivitiesDesignCompatibilityHost.cs",
                "tests/Elsa/Api/Compatibility/Testing/Http/HttpEvidenceCapture.cs",
                "tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs",
                "tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs",
                "tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs"
            },
            dependencies.Select(dependency => dependency.Path));
        Assert.All(dependencies, dependency =>
        {
            Assert.Matches("^[0-9a-f]{64}$", dependency.Sha256);
            Assert.DoesNotContain("bin/", dependency.Path, StringComparison.Ordinal);
            Assert.DoesNotContain("obj/", dependency.Path, StringComparison.Ordinal);
        });
        Assert.True(GitSucceeds("cat-file", "-e", $"{sourceCommit}^{{commit}}"));
        Assert.True(GitSucceeds("merge-base", "--is-ancestor", sourceCommit!, "HEAD"));
        Assert.Contains("FastEndpointsFeatureBase", GitText(sourceCommit!, "src/Elsa/Activities/Design/Api/ActivitiesDesignApiFeature.cs"), StringComparison.Ordinal);
        Assert.True(GitFileCount(sourceCommit!, "src/Elsa/Activities/Design/Api/Endpoints") >= 9);
        Assert.Equal(root.GetProperty("runnerFingerprint").GetString(), Fingerprint(dependencies));
        Assert.True(DependenciesMatch(sourceCommit!, dependencies));
    }

    [Fact]
    public void Receipt_rejects_mutated_dependency_and_fixture_metadata()
    {
        var directory = BaselineDirectory();
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(directory, ReceiptFileName)));
        var root = receipt.RootElement;
        var dependencies = ReadDependencies(root);
        var mutated = dependencies.Select((dependency, index) => index == 0
            ? dependency with { Sha256 = new string('0', 64) }
            : dependency).ToArray();

        Assert.True(DependenciesMatch(root.GetProperty("sourceCommit").GetString()!, dependencies));
        Assert.False(DependenciesMatch(root.GetProperty("sourceCommit").GetString()!, mutated));
        Assert.True(FixtureMetadataMatches(root, directory));
        Assert.False(FixtureMetadataMatches(root, directory, new string('0', 64)));
    }

    [Fact]
    public void Activities_design_fixture_mutations_are_detected_without_exact_approvals()
    {
        var directory = BaselineDirectory();
        var http = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(directory, HttpFileName));
        var openApi = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(directory, OpenApiFileName));
        var mutatedHttp = http.Select((observation, index) => index == 0
            ? observation with { Body = observation.Body + "<mutation>" }
            : observation).ToArray();
        var mutatedOperations = openApi.Operations.Select((operation, index) => index == 0
            ? operation with { Responses = operation.Responses + "<mutation>" }
            : operation).ToArray();
        var baseline = new CompatibilityEvidenceSet { Http = http, OpenApi = openApi };

        Assert.True(CompatibilityComparer.Compare(baseline, baseline).IsCompatible);
        Assert.Contains(CompatibilityComparer.Compare(
            baseline,
            baseline with { Http = mutatedHttp }).Deltas,
            delta => delta.Facet == CompatibilityFacet.Body);
        Assert.Contains(CompatibilityComparer.Compare(
            baseline,
            baseline with { OpenApi = new OpenApiEvidenceDocument(mutatedOperations) }).Deltas,
            delta => delta.Facet == CompatibilityFacet.OpenApi);
    }

    private static string BaselineDirectory() => Path.Join(AppContext.BaseDirectory, "Baselines");

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static RunnerDependency[] ReadDependencies(JsonElement root) =>
        root.GetProperty("runnerDependencies").EnumerateArray()
            .Select(item => new RunnerDependency(
                item.GetProperty("identity").GetString()!,
                item.GetProperty("path").GetString()!,
                item.GetProperty("sha256").GetString()!))
            .ToArray();

    private static bool DependenciesMatch(string sourceCommit, IReadOnlyList<RunnerDependency> dependencies) =>
        dependencies.All(dependency =>
        {
            var current = dependency.Identity == "checked-in-commit";
            var expectedIdentity = current ? "checked-in-commit" : $"source-commit:{sourceCommit}";
            var commit = current ? "HEAD" : sourceCommit;
            return dependency.Identity == expectedIdentity && dependency.Sha256 == GitHash(commit, dependency.Path);
        });

    private static string Fingerprint(IEnumerable<RunnerDependency> dependencies) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', dependencies.Select(dependency => $"{dependency.Identity}|{dependency.Path}|{dependency.Sha256}")) + "\n")))
            .ToLowerInvariant();

    private static bool FixtureMetadataMatches(JsonElement receipt, string directory, string? httpHash = null) =>
        receipt.GetProperty("httpSha256").GetString() == (httpHash ?? Hash(Path.Join(directory, HttpFileName))) &&
        receipt.GetProperty("openApiSha256").GetString() == Hash(Path.Join(directory, OpenApiFileName)) &&
        receipt.GetProperty("rawOpenApiSha256").GetString() == Hash(Path.Join(directory, RawOpenApiFileName)) &&
        receipt.GetProperty("initialApprovalsSha256").GetString() == Hash(Path.Join(directory, InitialApprovalsFileName)) &&
        receipt.GetProperty("registrationCount").GetInt32() == 38 &&
        receipt.GetProperty("caseCount").GetInt32() == ActivitiesDesignCompatibilityCases.All.Count &&
        receipt.GetProperty("operationCount").GetInt32() == 38;

    private static string GitHash(string commit, string path) =>
        Convert.ToHexString(SHA256.HashData(GitBytes("show", $"{commit}:{path}"))).ToLowerInvariant();

    private static string GitText(string commit, string path) => Encoding.UTF8.GetString(GitBytes("show", $"{commit}:{path}"));

    private static int GitFileCount(string commit, string path) =>
        Encoding.UTF8.GetString(GitBytes("ls-tree", "-r", "--name-only", commit, path))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool GitSucceeds(params string[] arguments)
    {
        using var process = Process.Start(CreateGitStartInfo(arguments))!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static byte[] GitBytes(params string[] arguments)
    {
        using var process = Process.Start(CreateGitStartInfo(arguments))!;
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output.ToArray();
    }

    private static ProcessStartInfo CreateGitStartInfo(IReadOnlyCollection<string> arguments) => new("git", arguments)
    {
        WorkingDirectory = RepositoryRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    private static string RepositoryRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !Directory.Exists(Path.Join(current.FullName, ".git")) && !File.Exists(Path.Join(current.FullName, ".git")))
                current = current.Parent;
            return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        }
    }

    private sealed record RunnerDependency(string Identity, string Path, string Sha256);
}

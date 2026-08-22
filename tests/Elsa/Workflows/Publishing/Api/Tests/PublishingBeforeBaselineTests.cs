using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Structural gates for the immutable FastEndpoints-before route inventory.</summary>
public sealed class PublishingBeforeBaselineTests
{
    private const string HttpFileName = "publishing-http-fastendpoints.json";
    private const string OpenApiFileName = "publishing-openapi-fastendpoints.json";
    private const string RawOpenApiFileName = "publishing-openapi-fastendpoints.raw.json";
    private const string ReceiptFileName = "publishing-before-capture-receipt.json";
    private const string InitialApprovalsFileName = "publishing-approved-differences.initial.json";

    [Fact]
    public void Reviewed_manifest_contains_exactly_23_one_to_one_registrations()
    {
        var routes = PublishingCompatibilityCases.Manifest;

        Assert.Equal(23, routes.Count);
        Assert.Equal(23, routes.Select(route => route.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(23, routes.Select(route => route.Endpoint).Distinct().Count());
        Assert.Equal(23, routes.Select(route => route.Endpoint.ToString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(routes, route => Assert.False(string.IsNullOrWhiteSpace(route.Request)));
        Assert.All(routes, route => Assert.False(string.IsNullOrWhiteSpace(route.Response)));
        Assert.Equal(12, routes.Count(route => route.Action == "read"));
        Assert.Equal(11, routes.Count(route => route.Action == "manage"));
        Assert.Equal(23, PublishingCompatibilityCases.Anonymous.Count);
        Assert.Equal(23, PublishingCompatibilityCases.Authenticated.Count);
        Assert.Equal(10, PublishingCompatibilityCases.Binding.Count);
        Assert.Equal(17, PublishingCompatibilityCases.Domain.Count);
        Assert.Single(PublishingCompatibilityCases.Cancellation);
        Assert.Equal(74, PublishingCompatibilityCases.All.Count);
    }

    [Fact]
    public void Manifest_preserves_reserved_drafts_and_mixed_owner_prefixes()
    {
        var routes = PublishingCompatibilityCases.Manifest;
        var versioned = routes.Where(route => route.Endpoint.Route.Value.Contains("regex", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, versioned.Length);
        Assert.All(versioned, route => Assert.Contains("drafts", route.Endpoint.Route.Value, StringComparison.Ordinal));
        Assert.Equal(20, routes.Count(route => route.Endpoint.Route.Value.StartsWith("/publishing/", StringComparison.Ordinal)));
        Assert.Equal(3, routes.Count(route => route.Endpoint.Route.Value.StartsWith("/design/activities/", StringComparison.Ordinal)));
        Assert.Equal(4, routes.Count(route => route.Endpoint.Route.Value.StartsWith("/publishing/activity-", StringComparison.Ordinal)));
    }

    [Fact]
    public void Every_route_has_anonymous_and_authenticated_capture_cases()
    {
        var anonymous = PublishingCompatibilityCases.Anonymous.Select(testCase => testCase.Case.Split('|')[0]).ToHashSet(StringComparer.Ordinal);
        var authenticated = PublishingCompatibilityCases.Authenticated.Select(testCase => testCase.Case.Split('|')[0]).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(PublishingCompatibilityCases.Manifest.Select(route => route.Id).ToHashSet(StringComparer.Ordinal), anonymous);
        Assert.Equal(anonymous, authenticated);
        Assert.All(PublishingCompatibilityCases.Authenticated, testCase =>
            Assert.Contains("trusted-success", testCase.Case, StringComparison.Ordinal));
    }

    [Fact]
    public void Historical_http_fixture_covers_every_route_and_behavior_category()
    {
        var observations = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory, HttpFileName));

        Assert.Equal(74, observations.Length);
        var anonymous = observations.Where(item => item.Case.EndsWith("|anonymous", StringComparison.Ordinal)).ToArray();
        var authenticated = observations.Where(item => item.Case.EndsWith("|trusted-success", StringComparison.Ordinal)).ToArray();
        Assert.Equal(23, anonymous.Length);
        Assert.All(anonymous, item => Assert.Equal(401, item.StatusCode));
        Assert.Equal(23, authenticated.Length);
        foreach (var route in PublishingCompatibilityCases.Manifest)
        {
            var observation = Assert.Single(authenticated, item => item.Case == route.Id + "|trusted-success");
            Assert.Equal(route.SuccessStatus, observation.StatusCode);
        }

        AssertCase(observations, "WorkflowSnapshotPreflight|trusted-malformed-json", 400, "Completed");
        AssertCase(observations, "WorkflowPublish|trusted-null-body", 201, "Completed");
        AssertCase(observations, "ActivityDrafts.Publish|trusted-empty-body", 400, "Completed");
        AssertCase(observations, "ActivityDrafts.Publish|trusted-missing-body", 415, "Completed");
        AssertCase(observations, "ActivityDrafts.Publish|trusted-absent-content-type", 415, "Completed");
        AssertCase(observations, "ActivityDrafts.Publish|trusted-null-json-body", 201, "Completed");
        AssertCase(observations, "ActivityDrafts.Preflight|trusted-wrong-content-type", 415, "Completed");
        AssertCase(observations, "WorkflowPublish|trusted-route-over-body", 201, "Completed");
        AssertCase(observations, "WorkflowTestRuns.StartDraft|trusted-reserved-drafts-selection", 200, "Completed");
        AssertCase(observations, "ActivityTestRuns.Start|trusted-route-over-body", 202, "Completed");
        AssertCase(observations, "PublicationSlots.Get|trusted-domain-not-found", 404, "Completed");
        AssertCase(observations, "WorkflowPolicy.Set|trusted-domain-conflict", 409, "Completed");
        AssertCase(observations, "WorkflowPublish|trusted-domain-conflict", 409, "Completed");
        AssertCase(observations, "WorkflowPreflight|trusted-domain-validation", 400, "Completed");
        AssertCase(observations, "RuntimePreflight|trusted-domain-unavailable", 400, "Completed");
        AssertCase(observations, "ActivityDrafts.Preflight|trusted-unprocessable", 422, "Completed");
        AssertCase(observations, "WorkflowPublish|trusted-expression-errors", 422, "Completed");
        AssertCase(observations, "WorkflowPublish|trusted-expression-unavailable", 503, "Completed");
        AssertCase(observations, "WorkflowPublish|trusted-generic-500", 500, "Completed");
        AssertCase(observations, "WorkflowPreflight|trusted-sender-failure-not-invoked", 200, "Completed");
        AssertCase(observations, "RuntimePreflight|trusted-generic-500", 500, "Completed");
        AssertCase(observations, "ActivityPublications.GetReceipt|trusted-generic-500", 500, "Completed");
        AssertCase(observations, "ActivityTestRuns.Get|trusted-generic-500", 500, "Completed");
        AssertCase(observations, "PublicationSlots.Unpublish|trusted-slot-lifecycle", 200, "Completed");
        AssertCase(observations, "PublicationSlots.Restore|trusted-slot-lifecycle", 200, "Completed");
        AssertCase(observations, "ActivityTestRuns.GetByIdempotencyKey|trusted-test-run-lookup", 200, "Completed");
        AssertCase(observations, "ActivityTestRuns.Cancel|trusted-test-run-cancellation", 202, "Completed");
        AssertCase(observations, "ActivityTestRuns.Get|trusted-cancellation", 0, "Faulted:System.OperationCanceledException");
    }

    [Fact]
    public void Historical_openapi_fixture_contains_exactly_the_reviewed_23_operations()
    {
        var document = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, OpenApiFileName));
        var expected = PublishingCompatibilityCases.Manifest
            .Select(route => OpenApiIdentity(route.Endpoint.ToString()))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = document.Operations.Select(operation => operation.Endpoint.ToString()).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(23, document.Operations.Count);
        Assert.Equal(23, document.Operations.Select(operation => operation.Endpoint).Distinct().Count());
        Assert.Equal(expected, actual);
        Assert.All(document.Operations, operation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.OperationId));
            Assert.NotEqual("[]", operation.Tags);
            Assert.NotEmpty(operation.Responses);
            Assert.NotEmpty(operation.Schemas);
        });
    }

    [Fact]
    public void Receipt_hashes_every_fixture_and_pins_the_before_source()
    {
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(BaselineDirectory, ReceiptFileName)));
        var root = receipt.RootElement;
        var sourceCommit = root.GetProperty("sourceCommit").GetString()!;

        Assert.Equal(23, root.GetProperty("registrationCount").GetInt32());
        Assert.Equal(74, root.GetProperty("caseCount").GetInt32());
        Assert.Equal(23, root.GetProperty("operationCount").GetInt32());
        Assert.Equal(Hash(Path.Join(BaselineDirectory, HttpFileName)), root.GetProperty("httpSha256").GetString());
        Assert.Equal(Hash(Path.Join(BaselineDirectory, OpenApiFileName)), root.GetProperty("openApiSha256").GetString());
        Assert.Equal(Hash(Path.Join(BaselineDirectory, RawOpenApiFileName)), root.GetProperty("rawOpenApiSha256").GetString());
        Assert.Equal(Hash(Path.Join(BaselineDirectory, InitialApprovalsFileName)), root.GetProperty("initialApprovalsSha256").GetString());
        Assert.Equal("[]", BaselineFile.LoadCanonical(Path.Join(BaselineDirectory, InitialApprovalsFileName)));
        Assert.Equal("ancestor-before-migration", root.GetProperty("sourceRelationship").GetString());
        Assert.Equal("checked-in-commit", root.GetProperty("runnerIdentity").GetString());
        Assert.Contains("PUBLISHING_BEFORE_COMMIT=", root.GetProperty("captureCommand").GetString(), StringComparison.Ordinal);
        Assert.True(GitSucceeds("merge-base", "--is-ancestor", sourceCommit, "HEAD"));
        Assert.Contains("FastEndpointsFeatureBase", GitText(sourceCommit, "src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs"), StringComparison.Ordinal);
        Assert.True(GitFileCount(sourceCommit, "src/Elsa/Workflows/Publishing/Api/Endpoints") >= 12);
    }

    [Fact]
    public void Receipt_dependencies_are_branch_durable_and_mutation_biting()
    {
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(BaselineDirectory, ReceiptFileName)));
        var root = receipt.RootElement;
        var sourceCommit = root.GetProperty("sourceCommit").GetString()!;
        var dependencies = ReadDependencies(root);
        var mutated = dependencies.Select((dependency, index) => index == 0
            ? dependency with { Sha256 = new string('0', 64) }
            : dependency).ToArray();

        Assert.Equal(9, dependencies.Length);
        Assert.All(dependencies, dependency => Assert.Matches("^[0-9a-f]{64}$", dependency.Sha256));
        Assert.Equal(root.GetProperty("runnerFingerprint").GetString(), Fingerprint(dependencies));
        Assert.True(DependenciesMatch(sourceCommit, dependencies));
        Assert.False(DependenciesMatch(sourceCommit, mutated));
        Assert.NotEqual(Fingerprint(dependencies), Fingerprint(mutated));
    }

    private static void AssertCase(
        IReadOnlyCollection<HttpCompatibilityObservation> observations,
        string name,
        int status,
        string terminalState)
    {
        var observation = Assert.Single(observations, item => item.Case == name);
        Assert.Equal(status, observation.StatusCode);
        Assert.Equal(terminalState, observation.TerminalState);
    }

    private static string OpenApiIdentity(string identity) =>
        identity.Replace("{param:regex(^(?!drafts$).+$)}", "{param}", StringComparison.Ordinal);

    private static RunnerDependency[] ReadDependencies(JsonElement root) =>
        root.GetProperty("runnerDependencies").EnumerateArray()
            .Select(item => new RunnerDependency(
                item.GetProperty("identity").GetString()!,
                item.GetProperty("path").GetString()!,
                item.GetProperty("sha256").GetString()!))
            .ToArray();

    private static bool DependenciesMatch(string sourceCommit, IReadOnlyCollection<RunnerDependency> dependencies) =>
        dependencies.All(dependency => dependency.Identity.StartsWith("source-commit:", StringComparison.Ordinal)
            ? dependency.Identity == $"source-commit:{sourceCommit}" && Hash(GitBytes(sourceCommit, dependency.Path)) == dependency.Sha256
            : dependency.Identity == "checked-in-commit" && Hash(Path.Join(RepositoryRoot, dependency.Path)) == dependency.Sha256);

    private static string Fingerprint(IEnumerable<RunnerDependency> dependencies) =>
        Hash(Encoding.UTF8.GetBytes(string.Join("\n", dependencies.Select(dependency =>
            $"{dependency.Identity}|{dependency.Path}|{dependency.Sha256}")) + "\n"));

    private static bool GitSucceeds(params string[] arguments) => RunGit(arguments).ExitCode == 0;

    private static string GitText(string commit, string path) =>
        Encoding.UTF8.GetString(GitBytes(commit, path));

    private static byte[] GitBytes(string commit, string path)
    {
        var result = RunGit(["show", $"{commit}:{path}"]);
        Assert.Equal(0, result.ExitCode);
        return result.Bytes;
    }

    private static int GitFileCount(string commit, string path)
    {
        var result = RunGit(["ls-tree", "-r", "--name-only", commit, "--", path]);
        Assert.Equal(0, result.ExitCode);
        return Encoding.UTF8.GetString(result.Bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static GitResult RunGit(IReadOnlyCollection<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();
        return new GitResult(process.ExitCode, output.ToArray());
    }

    private static string Hash(string path) => Hash(File.ReadAllBytes(path));

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string BaselineDirectory => Path.Join(AppContext.BaseDirectory, "Baselines");

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
    private sealed record GitResult(int ExitCode, byte[] Bytes);
}

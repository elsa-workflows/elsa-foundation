using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Expressions.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Get;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.List;

namespace Elsa.Workflows.Design.Api.Tests;

public sealed class WorkflowDesignApiBeforeBaselineTests
{
    [Fact]
    public void FastEndpoints_before_http_fixture_covers_exactly_all_27_registrations()
    {
        var observations = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp();
        Assert.Equal(78, observations.Count);
        Assert.Equal(
            WorkflowDesignCompatibilityCases.Anonymous.Select(testCase => testCase.Endpoint + "|" + testCase.Case).Order(),
            observations.Where(observation => WorkflowDesignCompatibilityCases.Anonymous.Any(testCase =>
                testCase.Endpoint + "|" + testCase.Case == observation.Endpoint + "|" + observation.Case))
                .Select(observation => observation.Endpoint + "|" + observation.Case).Order());
        Assert.All(observations.Where(observation => WorkflowDesignCompatibilityCases.Anonymous.Any(testCase =>
                testCase.Endpoint + "|" + testCase.Case == observation.Endpoint + "|" + observation.Case)), observation =>
        {
            Assert.Equal(401, observation.StatusCode);
            Assert.Equal("401", observation.Status);
            Assert.NotEmpty(observation.Binding);
        });
        Assert.Contains(observations, observation => observation.Case == "describe-expression-tooling|trusted-success" && observation.StatusCode == 200);
        Assert.Contains(observations, observation => observation.Case == "promote-draft|trusted-409-concurrency" && observation.StatusCode == 409);
        var permanentDeleteConflict = Assert.Single(observations, observation => observation.Case == "delete-definition-permanently|trusted-409-not-soft-deleted");
        Assert.Equal(409, permanentDeleteConflict.StatusCode);
        Assert.Equal("application/problem+json; charset=utf-8", permanentDeleteConflict.ContentType);
        Assert.Contains("must be soft-deleted", permanentDeleteConflict.ProblemDetails, StringComparison.Ordinal);
        Assert.Contains(observations, observation => observation.Case == "promotion-preflight|trusted-nonmutation" && observation.StatusCode == 200);
        foreach (var observation in observations.Where(observation => new[] { "delete-definition|", "delete-definition-permanently|", "restore-definition|", "discard-draft|" }.Any(prefix => observation.Case.StartsWith(prefix, StringComparison.Ordinal)) &&
                                                                       (observation.Case.Contains("missing-body", StringComparison.Ordinal) ||
                                                                        observation.Case.Contains("malformed-json", StringComparison.Ordinal) ||
                                                                        observation.Case.Contains("unsupported-content-type", StringComparison.Ordinal))))
        {
            if (observation.Case.Contains("malformed-json", StringComparison.Ordinal))
            {
                Assert.Equal(400, observation.StatusCode);
                Assert.Contains("serializerErrors", observation.ProblemDetails, StringComparison.Ordinal);
                Assert.Equal("application/problem+json; charset=utf-8", observation.ContentType);
            }
            else if (observation.Case.StartsWith("restore-definition|", StringComparison.Ordinal))
                Assert.Equal(415, observation.StatusCode);
            else
                Assert.Equal(204, observation.StatusCode);
        }
    }

    [Fact]
    public void FastEndpoints_before_openapi_fixture_covers_exactly_all_27_operations()
    {
        var operations = WorkflowDesignCompatibilityEvidence.LoadLegacyOpenApi().Operations;
        Assert.Equal(27, operations.Count);
        Assert.Equal(
            WorkflowDesignCompatibilityCases.Anonymous.Select(testCase => testCase.Endpoint.ToString()).Order(),
            operations.Select(operation => operation.Endpoint.ToString()).Order());
        Assert.All(operations, operation =>
        {
            Assert.NotEmpty(operation.Responses);
            Assert.NotEmpty(operation.Schemas);
        });
    }

    [Fact]
    public void Before_fixtures_are_byte_stable_and_have_provenance_hashes()
    {
        var directory = Path.Join(AppContext.BaseDirectory, "Baselines");
        var httpPath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.HttpFileName);
        var openApiPath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.OpenApiFileName);
        var tracePath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.HandlerTraceFileName);
        var provenancePath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.ProvenanceFileName);
        using var provenance = JsonDocument.Parse(BaselineFile.Read(provenancePath));
        var root = provenance.RootElement;

        Assert.Equal(27, root.GetProperty("registrationCount").GetInt32());
        Assert.Equal(Hash(httpPath), root.GetProperty("httpSha256").GetString());
        Assert.Equal(Hash(openApiPath), root.GetProperty("openApiSha256").GetString());
        Assert.Equal(Hash(tracePath), root.GetProperty("handlerTraceSha256").GetString());
        Assert.Equal("#1372", root.GetProperty("issue").GetString());
        Assert.Equal("67ba4b3b9bec3a6c2aac0d6d332099baf723e802", root.GetProperty("sourceCommit").GetString());
        Assert.Equal("checked-in-commit", root.GetProperty("captureRunnerIdentity").GetString());
        Assert.False(root.TryGetProperty("captureRunnerCommit", out _));
        Assert.Equal(
            "WORKFLOWS_DESIGN_BEFORE_COMMIT=67ba4b3b9bec3a6c2aac0d6d332099baf723e802 bash tools/compatibility/capture-workflows-design-before.sh",
            root.GetProperty("captureCommand").GetString());
        Assert.Equal(78, root.GetProperty("caseCount").GetInt32());
        Assert.Equal(27, root.GetProperty("operationCount").GetInt32());

        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(directory, WorkflowDesignCompatibilityEvidence.ReceiptFileName)));
        Assert.Equal(root.GetProperty("sourceCommit").GetString(), receipt.RootElement.GetProperty("sourceCommit").GetString());
        Assert.Equal(root.GetProperty("captureRunnerIdentity").GetString(), receipt.RootElement.GetProperty("runnerIdentity").GetString());
        Assert.Equal(root.GetProperty("runnerFingerprint").GetString(), receipt.RootElement.GetProperty("runnerFingerprint").GetString());
        AssertReceiptMetadataMatchesFixtures(receipt.RootElement, directory);
        Assert.Equal(
            DependencyFingerprints(root.GetProperty("runnerDependencies")),
            DependencyFingerprints(receipt.RootElement.GetProperty("runnerDependencies")));

        var firstHttp = CompatibilityJson.Serialize(BaselineFile.Load<object>(httpPath));
        var secondHttp = CompatibilityJson.Serialize(BaselineFile.Load<object>(httpPath));
        Assert.Equal(firstHttp, secondHttp);
    }

    [Fact]
    public void Historical_capture_receipt_pins_every_runner_dependency()
    {
        var directory = Path.Join(AppContext.BaseDirectory, "Baselines");
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(directory, WorkflowDesignCompatibilityEvidence.ReceiptFileName)));
        var root = receipt.RootElement;
        Assert.Equal("67ba4b3b9bec3a6c2aac0d6d332099baf723e802", root.GetProperty("sourceCommit").GetString());
        Assert.Equal("checked-in-commit", root.GetProperty("runnerIdentity").GetString());
        Assert.False(root.TryGetProperty("runnerCommit", out _));
        Assert.Equal(
            "WORKFLOWS_DESIGN_BEFORE_COMMIT=67ba4b3b9bec3a6c2aac0d6d332099baf723e802 bash tools/compatibility/capture-workflows-design-before.sh",
            root.GetProperty("captureCommand").GetString());
        var dependencies = root.GetProperty("runnerDependencies").EnumerateArray().ToArray();
        Assert.Equal(
            new[]
            {
                "tools/compatibility/capture-workflows-design-before.sh",
                "tools/compatibility/WorkflowsDesignFastEndpointsCapture/Program.cs",
                "tools/compatibility/WorkflowsDesignFastEndpointsCapture/WorkflowsDesignFastEndpointsCapture.csproj",
                "tools/compatibility/WorkflowsDesignFastEndpointsCapture/HistoricalOpenApiEvidenceCapture.cs",
                "tests/Elsa/Api/Compatibility/Testing/OpenApi/OpenApiEvidenceCapture.cs",
                "tests/Elsa/Api/Compatibility/Testing/Serialization/CompatibilityJson.cs",
                "tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointIdentity.cs"
            },
            dependencies.Select(dependency => dependency.GetProperty("path").GetString()).ToArray());
        Assert.All(dependencies, dependency => Assert.Matches("^[0-9a-f]{64}$", dependency.GetProperty("sha256").GetString()!));
        var sourceCommit = root.GetProperty("sourceCommit").GetString()!;
        Assert.True(IsCommitResolvable(sourceCommit));
        Assert.True(IsAncestor(sourceCommit, CurrentHead()));
        Assert.Contains("FastEndpointsFeatureBase", GitBlobText(sourceCommit, "src/Elsa/Workflows/Design/Api/WorkflowsDesignApiFeature.cs"));
        Assert.True(GitPathCount(sourceCommit, "src/Elsa/Workflows/Design/Api/Endpoints") >= 25);
        Assert.Equal(root.GetProperty("runnerFingerprint").GetString(), RunnerFingerprint(ReadRunnerDependencies(root)));
        Assert.True(RunnerDependenciesMatch(root));
    }

    [Fact]
    public void Historical_capture_receipt_rejects_a_mutated_runner_dependency_hash()
    {
        var directory = Path.Join(AppContext.BaseDirectory, "Baselines");
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(directory, WorkflowDesignCompatibilityEvidence.ReceiptFileName)));
        var root = receipt.RootElement;
        var dependencies = ReadRunnerDependencies(root);
        var mutated = dependencies.Select((dependency, index) => index == 0
            ? dependency with { Sha256 = new string('0', 64) }
            : dependency).ToArray();

        Assert.True(RunnerDependenciesMatch(root));
        Assert.False(RunnerDependenciesMatch(root, mutated));
    }

    [Fact]
    public void Historical_capture_receipt_rejects_mutated_fixture_metadata()
    {
        var directory = Path.Join(AppContext.BaseDirectory, "Baselines");
        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(directory, WorkflowDesignCompatibilityEvidence.ReceiptFileName)));

        AssertReceiptMetadataMatchesFixtures(receipt.RootElement, directory);
        Assert.False(ReceiptMetadataMatchesFixtures(
            receipt.RootElement,
            directory,
            expectedHttpSha256: new string('0', 64)));
    }

    private static string GitBlobHash(string commit, string path)
    {
        var startInfo = new ProcessStartInfo("git", ["show", $"{commit}:{path}"])
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        using var contents = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(contents);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contents.ToArray())).ToLowerInvariant();
    }

    private static string GitBlobText(string commit, string path)
    {
        var startInfo = new ProcessStartInfo("git", ["show", $"{commit}:{path}"])
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        using var contents = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(contents);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return Encoding.UTF8.GetString(contents.ToArray());
    }

    private static int GitPathCount(string commit, string path)
    {
        var startInfo = new ProcessStartInfo("git", ["ls-tree", "-r", "--name-only", commit, path])
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static bool IsCommitResolvable(string commit)
    {
        var startInfo = new ProcessStartInfo("git", ["cat-file", "-e", $"{commit}^{{commit}}"])
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return process.ExitCode == 0;
    }

    private static string CurrentHead()
    {
        var startInfo = new ProcessStartInfo("git", ["rev-parse", "HEAD"])
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output.Trim();
    }

    private static bool IsAncestor(string ancestor, string descendant)
    {
        var startInfo = new ProcessStartInfo("git", ["merge-base", "--is-ancestor", ancestor, descendant])
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.True(process.ExitCode is 0 or 1, process.StandardError.ReadToEnd());
        return process.ExitCode == 0;
    }

    private static RunnerDependencySnapshot[] ReadRunnerDependencies(JsonElement root) =>
        root.GetProperty("runnerDependencies").EnumerateArray()
            .Select(dependency => new RunnerDependencySnapshot(
                dependency.GetProperty("identity").GetString()!,
                dependency.GetProperty("path").GetString()!,
                dependency.GetProperty("sha256").GetString()!))
            .ToArray();

    private static string[] DependencyFingerprints(JsonElement dependencies) =>
        dependencies.EnumerateArray()
            .Select(dependency => $"{dependency.GetProperty("identity").GetString()}|{dependency.GetProperty("path").GetString()}|{dependency.GetProperty("sha256").GetString()}")
            .ToArray();

    private static string RunnerFingerprint(IEnumerable<RunnerDependencySnapshot> dependencies) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", dependencies.Select(dependency =>
                $"{dependency.Identity}|{dependency.Path}|{dependency.Sha256}")) + "\n"))).ToLowerInvariant();

    private static bool RunnerDependenciesMatch(
        JsonElement root,
        IReadOnlyList<RunnerDependencySnapshot>? dependencies = null)
    {
        var sourceCommit = root.GetProperty("sourceCommit").GetString()!;
        return (dependencies ?? ReadRunnerDependencies(root)).All(dependency =>
        {
            var expectedIdentity = dependency.Path.StartsWith("tools/compatibility/", StringComparison.Ordinal)
                ? "checked-in-commit"
                : $"source-commit:{sourceCommit}";
            var expectedHash = dependency.Identity == "checked-in-commit"
                ? GitBlobHash(CurrentHead(), dependency.Path)
                : GitBlobHash(sourceCommit, dependency.Path);
            return dependency.Identity == expectedIdentity && dependency.Sha256 == expectedHash;
        });
    }

    private static void AssertReceiptMetadataMatchesFixtures(JsonElement receipt, string directory) =>
        Assert.True(ReceiptMetadataMatchesFixtures(receipt, directory));

    private static bool ReceiptMetadataMatchesFixtures(
        JsonElement receipt,
        string directory,
        string? expectedHttpSha256 = null)
    {
        var httpPath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.HttpFileName);
        var openApiPath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.OpenApiFileName);
        var tracePath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.HandlerTraceFileName);
        var http = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp();
        var openApi = WorkflowDesignCompatibilityEvidence.LoadLegacyOpenApi();
        return receipt.GetProperty("httpSha256").GetString() == (expectedHttpSha256 ?? Hash(httpPath)) &&
               receipt.GetProperty("openApiSha256").GetString() == Hash(openApiPath) &&
               receipt.GetProperty("traceSha256").GetString() == Hash(tracePath) &&
               receipt.GetProperty("registrationCount").GetInt32() == 27 &&
               receipt.GetProperty("caseCount").GetInt32() == http.Count &&
               receipt.GetProperty("operationCount").GetInt32() == openApi.Operations.Count;
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }

    [Fact]
    public async Task Minimal_api_after_evidence_matches_the_immutable_fastendpoints_before_surface()
    {
        await using var host = await WorkflowDesignCompatibilityHost.StartAsync();
        var afterHttp = (await Task.WhenAll(WorkflowDesignCompatibilityCases.All.Select(testCase =>
            HttpEvidenceCapture.CaptureAsync(host.Client, testCase)))).ToArray();
        var afterOpenApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync(), includeIdentityMetadata: true);

        var comparison = CompatibilityComparer.CompareBidirectional(
            new CompatibilityEvidenceSet
            {
                Http = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp(),
                OpenApi = WorkflowDesignCompatibilityEvidence.LoadLegacyOpenApi()
            },
            new CompatibilityEvidenceSet { Http = afterHttp, OpenApi = afterOpenApi },
            WorkflowDesignCompatibilityEvidence.LoadApprovals());

        Assert.True(comparison.IsCompatible, string.Join(Environment.NewLine, comparison.Failures));
    }

    [Fact]
    public void Compatibility_comparer_rejects_an_unused_approval()
    {
        var before = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp();
        var approval = new ApprovedDifference
        {
            Endpoint = "/design/workflows/definitions",
            Method = "GET",
            Case = "list-definitions",
            Facet = CompatibilityFacet.Status,
            Expected = "401",
            Actual = "403",
            Owner = "Elsa.Workflows.Design.Api",
            Reason = "sentinel approval must not be ignored",
            FollowUp = "#1372"
        };

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = before },
            [approval]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.InvalidApprovals, issue => issue.StartsWith("Unused approved difference:", StringComparison.Ordinal));
    }

    [Fact]
    public void Bidirectional_comparer_rejects_a_one_sided_approval()
    {
        var before = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp();
        var after = before.Select(observation => observation with
        {
            StatusCode = observation.Case == "list-definitions" ? StatusCodes.Status403Forbidden : observation.StatusCode
        }).ToArray();
        var approval = new ApprovedDifference
        {
            Endpoint = "/design/workflows/definitions",
            Method = "GET",
            Case = "list-definitions",
            Facet = CompatibilityFacet.Status,
            Expected = "401",
            Actual = "403",
            Owner = "Elsa.Workflows.Design.Api",
            Reason = "sentinel one-sided approval",
            FollowUp = "#1372"
        };

        var result = CompatibilityComparer.CompareBidirectional(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = after },
            [approval]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Deltas, delta => delta.Case == "list-definitions" && delta.Expected == "403" && delta.Actual == "401");
    }

    [Fact]
    public void Bidirectional_comparer_consumes_an_exact_two_sided_approval_pair()
    {
        var before = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp();
        var after = before.Select(observation => observation.Case == "list-definitions"
            ? observation with { StatusCode = StatusCodes.Status403Forbidden }
            : observation).ToArray();
        var approval = new ApprovedDifference
        {
            Endpoint = "/design/workflows/definitions",
            Method = "GET",
            Case = "list-definitions",
            Facet = CompatibilityFacet.Status,
            Expected = "401",
            Actual = "403",
            Owner = "Elsa.Workflows.Design.Api",
            Reason = "sentinel two-sided approval",
            FollowUp = "#1372"
        };

        var result = CompatibilityComparer.CompareBidirectional(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = after },
            [approval, approval with { Reverse = true }]);

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public void Compatibility_comparer_rejects_a_mutated_fixture_without_an_approval()
    {
        var before = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp();
        var mutated = before.Select(observation => observation.Case == "list-definitions"
            ? observation with { Body = "mutated" }
            : observation).ToArray();

        var result = CompatibilityComparer.CompareBidirectional(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = mutated });

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Deltas, delta => delta.Case == "list-definitions" && delta.Facet == CompatibilityFacet.Body);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record RunnerDependencySnapshot(string Identity, string Path, string Sha256);

    internal sealed class WorkflowDesignCompatibilityHost(IHost host) : IAsyncDisposable
    {
        public HttpClient Client { get; } = host.GetTestClient();

        public static async Task<WorkflowDesignCompatibilityHost> StartAsync()
        {
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.UseSetting(WebHostDefaults.ApplicationKey, "workflows-design-compatibility");
                    webHost.ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddHttpContextAccessor();
                        services.AddAuthentication(CompatibilityAuthenticationHandler.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, CompatibilityAuthenticationHandler>(CompatibilityAuthenticationHandler.SchemeName, _ => { });
                        services.AddAuthorization();
                        services.AddFoundationIdentityAbstractions(options =>
                            options.NormalizedAuthenticationTypes = new HashSet<string>([CompatibilityAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                        services.AddOpenApi();
                        new WorkflowsDesignApiFeature().ConfigureServices(services);
                        services.AddSingleton<IExpressionToolingProviderResolver, EmptyExpressionToolingProviderResolver>();
                        services.AddSingleton<IActivityDefinitionVersionStore, BaselineActivityDefinitionVersionStore>();
                        // No mediator senders: every Workflows Design route now owns its handling.
                        Support.DefinitionDomainFakes.Register(services);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            WorkflowsDesignApi.MapWorkflowsDesignApi(endpoints);
                            endpoints.MapOpenApi();
                        });
                    });
                })
                .Build();
            await host.StartAsync();
            return new WorkflowDesignCompatibilityHost(host);
        }

        public async Task<string> GetOpenApiAsync()
        {
            using var response = await Client.GetAsync("/openapi/v1.json");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class CompatibilityAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "WorkflowsDesignCompatibility";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[WorkflowDesignCompatibilityCases.IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new ClaimsIdentity(Scheme.Name);
            if (identity == "trusted" || identity.StartsWith("trusted-", StringComparison.Ordinal))
            {
                claims.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
                claims.AddClaim(new Claim(IdentityClaimTypes.Permission,
                    identity is "trusted" or "trusted-success" ? PermissionKey.Wildcard : identity.Contains("manage", StringComparison.Ordinal) || identity.Contains("promote", StringComparison.Ordinal) || identity.Contains("delete", StringComparison.Ordinal) || identity.Contains("preflight", StringComparison.Ordinal) ? WorkflowDesignPermissions.Manage : WorkflowDesignPermissions.Read));
                claims.AddClaim(new Claim(IdentityClaimTypes.TenantId, "tenant-design"));
            }
            else
                claims.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name)));
        }
    }

    private sealed class EmptyExpressionToolingProviderResolver : IExpressionToolingProviderResolver
    {
        public IExpressionToolingProvider? Find(string expressionType) => null;
    }

    private sealed class BaselineActivityDefinitionVersionStore : IActivityDefinitionVersionStore
    {
        private static InvalidOperationException NotExecuted() => new("The historical compatibility path did not execute an activity-definition store operation.");

        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw NotExecuted();
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw NotExecuted();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw NotExecuted();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw NotExecuted();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw NotExecuted();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw NotExecuted();
    }
}

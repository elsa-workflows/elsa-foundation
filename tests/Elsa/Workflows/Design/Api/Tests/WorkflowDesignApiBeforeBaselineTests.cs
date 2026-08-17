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
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Tests.Support;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
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
        Assert.Equal("3c7ec1abb0de7d3e726d74bd182be6fce6ff6e93", root.GetProperty("captureRunnerCommit").GetString());
        Assert.Equal(
            "WORKFLOWS_DESIGN_BEFORE_COMMIT=67ba4b3b9bec3a6c2aac0d6d332099baf723e802 WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT=3c7ec1abb0de7d3e726d74bd182be6fce6ff6e93 bash tools/compatibility/capture-workflows-design-before.sh",
            root.GetProperty("captureCommand").GetString());
        Assert.Equal(78, root.GetProperty("caseCount").GetInt32());
        Assert.Equal(27, root.GetProperty("operationCount").GetInt32());

        using var receipt = JsonDocument.Parse(BaselineFile.Read(Path.Join(directory, WorkflowDesignCompatibilityEvidence.ReceiptFileName)));
        Assert.Equal(root.GetProperty("sourceCommit").GetString(), receipt.RootElement.GetProperty("sourceCommit").GetString());
        Assert.Equal(root.GetProperty("captureRunnerCommit").GetString(), receipt.RootElement.GetProperty("runnerCommit").GetString());
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
        Assert.Equal("3c7ec1abb0de7d3e726d74bd182be6fce6ff6e93", root.GetProperty("runnerCommit").GetString());
        Assert.Equal(
            "WORKFLOWS_DESIGN_BEFORE_COMMIT=67ba4b3b9bec3a6c2aac0d6d332099baf723e802 WORKFLOWS_DESIGN_CAPTURE_RUNNER_COMMIT=3c7ec1abb0de7d3e726d74bd182be6fce6ff6e93 bash tools/compatibility/capture-workflows-design-before.sh",
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
        foreach (var dependency in dependencies)
        {
            var path = dependency.GetProperty("path").GetString()!;
            var expectedCommit = path.StartsWith("tools/compatibility/", StringComparison.Ordinal)
                ? root.GetProperty("runnerCommit").GetString()!
                : root.GetProperty("sourceCommit").GetString()!;
            Assert.Equal(expectedCommit, dependency.GetProperty("commit").GetString());
            Assert.Equal(GitBlobHash(expectedCommit, path), dependency.GetProperty("sha256").GetString());
        }
    }

    private static string GitBlobHash(string commit, string path)
    {
        var startInfo = new ProcessStartInfo("git", ["show", $"{commit}:{path}"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        var contents = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(contents))).ToLowerInvariant();
    }

    private static string[] DependencyFingerprints(JsonElement dependencies) =>
        dependencies.EnumerateArray()
            .Select(dependency => $"{dependency.GetProperty("commit").GetString()}|{dependency.GetProperty("path").GetString()}|{dependency.GetProperty("sha256").GetString()}")
            .ToArray();

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

    private sealed class WorkflowDesignCompatibilityHost(IHost host) : IAsyncDisposable
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
                        services.AddSingleton<BaselineRequestSender>();
                        services.AddSingleton<IRequestSender>(provider => provider.GetRequiredService<BaselineRequestSender>());
                        services.AddSingleton<ICommandSender, BaselineCommandSender>();
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

    private sealed class BaselineRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            var scenario = contextAccessor.HttpContext?.Request.Headers[WorkflowDesignCompatibilityCases.IdentityHeader].ToString();
            if (request is ListDefinitions && scenario == "trusted-paging")
                return Task.FromResult((T)(object)new WorkflowDefinitionListView([]));
            if (request is PreflightDraftPromotion && scenario == "trusted-preflight")
                return Task.FromResult((T)(object)new PromotionPreflightAssessmentView(true, "exact", "1.0.0", "1.0.0", "1.0.0", []));
            if (request is GetDefinition && scenario == "trusted-not-found")
                throw new EntityNotFoundException("definition sample was not found");
            return Task.FromResult(default(T)!);
        }
    }

    private sealed class BaselineCommandSender(IHttpContextAccessor contextAccessor) : ICommandSender
    {
        private string Scenario => contextAccessor.HttpContext?.Request.Headers[WorkflowDesignCompatibilityCases.IdentityHeader].ToString() ?? "";

        public Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
            Scenario switch
            {
                "trusted-promote-404" => throw new EntityNotFoundException("draft sample was not found"),
                "trusted-promote-409" => throw new WorkflowDefinitionVersionConflictException("definition sample", "1.0.0"),
                "trusted-promote-500" => throw new InvalidOperationException("deterministic command failure"),
                _ => Task.FromResult(default(T)!)
            };

        public Task Send(ICommand command, CancellationToken cancellationToken = default) =>
            Scenario switch
            {
                "trusted-delete-404" => throw new EntityNotFoundException("definition sample was not found"),
                "trusted-delete-501" => throw new PermanentDeletionUnavailableException("sample"),
                "trusted-delete-409" => throw new WorkflowDefinitionNotSoftDeletedException("sample"),
                "trusted-delete-500" => throw new InvalidOperationException("deterministic command failure"),
                _ => Task.CompletedTask
            };
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

using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Attention.Api;
using Elsa.Attention.Core;
using Elsa.Expressions.Api;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Runtime.JavaScript;
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
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave1HostCollection.Name)]
public sealed class Wave1MinimalApiCompatibilityTests
{
    private static readonly string BaselineDirectory = Path.Join(AppContext.BaseDirectory, "Baselines");

    [Fact]
    public async Task Minimal_api_after_evidence_matches_immutable_fastendpoints_before_evidence()
    {
        var beforeHttp = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory, "wave1-http-fastendpoints.json"));
        var beforeOpenApi = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave1-openapi-fastendpoints.json"));

        await using var host = await Wave1MinimalApiHost.StartAsync();
        var afterHttp = Wave1Cases.All.Select(testCase => HttpEvidenceCapture.CaptureAsync(host.Client, testCase));
        var afterHttpEvidence = (await Task.WhenAll(afterHttp)).ToArray();
        var afterOpenApiEvidence = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());

        Assert.Equal(Wave1Cases.All.Count, afterHttpEvidence.Length);
        Assert.Equal(8, afterOpenApiEvidence.Operations.Count);

        Assert.Equal(beforeHttp.Select(item => item.Endpoint + "|" + item.Case), afterHttpEvidence.Select(item => item.Endpoint + "|" + item.Case));

        var registry = BaselineFile.Load<ApprovedDifference[]>(Path.Join(BaselineDirectory, "rest-compatibility-approved-differences.json"));
        var wave1Routes = beforeOpenApi.Operations.Select(operation => operation.Endpoint.Route.Value).ToHashSet(StringComparer.Ordinal);
        var wave1Approvals = registry.Where(approval => wave1Routes.Contains(approval.Endpoint)).ToArray();
        Assert.Equal(2, wave1Approvals.Length);
        Assert.Equal(
            ["/javascript/documents/render", "/javascript/execute"],
            wave1Approvals.Select(approval => approval.Endpoint).Order(StringComparer.Ordinal));
        Assert.All(wave1Approvals, approval =>
        {
            Assert.Equal("openapi", approval.Facet);
            Assert.Equal("openapi", approval.Case);
            Assert.Equal(
                approval.Actual,
                afterOpenApiEvidence.Operations.Single(operation => operation.Endpoint.Route.Value == approval.Endpoint && operation.Endpoint.Method.Value == approval.Method).Canonical);
        });
        var comparison = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = beforeHttp, OpenApi = beforeOpenApi },
            new CompatibilityEvidenceSet { Http = afterHttpEvidence, OpenApi = afterOpenApiEvidence },
            wave1Approvals);

        Assert.True(comparison.IsCompatible, string.Join(Environment.NewLine, comparison.Failures));
    }

    private static class Wave1Cases
    {
        public static IReadOnlyList<HttpCompatibilityCase> All { get; } =
        [
            new(new("/capabilities", "GET"), "success", () => Request(HttpMethod.Get, "/capabilities", "wildcard")) { Binding = "" },
            new(new("/_elsa/attention/items", "GET"), "success", () => Request(HttpMethod.Get, "/_elsa/attention/items", "wildcard")) { Binding = "" },
            new(new("/_elsa/attention/items", "GET"), "invalid-filter", () => Request(HttpMethod.Get, "/_elsa/attention/items?contributorId=", "wildcard")) { Binding = "query=contributorId" },
            new(new("/expressions/descriptors", "GET"), "success", () => Request(HttpMethod.Get, "/expressions/descriptors", "wildcard")) { Binding = "" },
            new(new("/expressions/variable-types", "GET"), "success", () => Request(HttpMethod.Get, "/expressions/variable-types", "wildcard")) { Binding = "" },
            new(new("/javascript/documents/render", "GET"), "success", () => Request(HttpMethod.Get, "/javascript/documents/render", "wildcard")) { Binding = "" },
            new(new("/javascript/execute", "POST"), "success", () => Json(HttpMethod.Post, "/javascript/execute", "{\"script\":\"return 1;\"}")) { Binding = "body=script" },
            new(new("/javascript/execute", "POST"), "invalid-json", () => Json(HttpMethod.Post, "/javascript/execute", "{invalid")) { Binding = "body=script" },
            new(new("/javascript/execute", "POST"), "missing-body", () => Request(HttpMethod.Post, "/javascript/execute", "wildcard", new StringContent("", Encoding.UTF8, "application/json"))) { Binding = "body=script" },
            new(new("/javascript/execute", "POST"), "blank-script", () => Json(HttpMethod.Post, "/javascript/execute", "{\"script\":\"\"}")) { Binding = "body=script" },
            new(new("/_elsa/workflows/dashboard/definitions", "GET"), "success", () => Request(HttpMethod.Get, "/_elsa/workflows/dashboard/definitions", "wildcard")) { Binding = "" },
            new(new("/_elsa/workflows/dashboard/definitions", "GET"), "missing-tenant", () => Request(HttpMethod.Get, "/_elsa/workflows/dashboard/definitions", "no-tenant")) { Binding = "" },
            new(new("/_elsa/workflows/dashboard/runs", "GET"), "success", () => Request(HttpMethod.Get, "/_elsa/workflows/dashboard/runs?from=2026-08-15T00:00:00Z&to=2026-08-15T01:00:00Z&bucket=hour", "wildcard")) { Binding = "query=from,to,bucket" },
            new(new("/_elsa/workflows/dashboard/runs", "GET"), "invalid-query", () => Request(HttpMethod.Get, "/_elsa/workflows/dashboard/runs?from=bad&to=2026-08-15T01:00:00Z&bucket=hour", "wildcard")) { Binding = "query=from,to,bucket" }
        ];

        private static HttpRequestMessage Json(HttpMethod method, string path, string body) =>
            Request(method, path, "wildcard", new StringContent(body, Encoding.UTF8, "application/json"));

        private static HttpRequestMessage Request(HttpMethod method, string path, string identity, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, path) { Content = content };
            request.Headers.TryAddWithoutValidation(Wave1MinimalApiHost.IdentityHeader, identity);
            return request;
        }
    }

    private sealed class Wave1MinimalApiHost : IAsyncDisposable
    {
        public const string IdentityHeader = "X-Wave1-Identity";
        private readonly IHost host;
        public HttpClient Client { get; }

        private Wave1MinimalApiHost(IHost host)
        {
            this.host = host;
            Client = host.GetTestClient();
        }

        public static async Task<Wave1MinimalApiHost> StartAsync()
        {
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.UseSetting(WebHostDefaults.ApplicationKey, "testhost");
                    webHost.ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthentication(Wave1Auth.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, Wave1Auth>(Wave1Auth.SchemeName, _ => { });
                        services.AddAuthorization();
                        services.AddOpenApi();
                        services.AddFoundationIdentityAbstractions(options =>
                            options.NormalizedAuthenticationTypes = new HashSet<string>([Wave1Auth.SchemeName], StringComparer.Ordinal));
                        new ApiCapabilitiesFeature().ConfigureServices(services);
                        new AttentionApiFeature().ConfigureServices(services);
                        new ExpressionsApiFeature().ConfigureServices(services);
                        new JavaScriptRenderingEndpointsFeature().ConfigureServices(services);
                        new JavaScriptActivitiesEndpointsFeature().ConfigureServices(services);
                        new WorkflowsDashboardFeature().ConfigureServices(services);
                        services.AddSingleton<IApiCapabilityCatalog, CapabilityCatalog>();
                        services.AddSingleton<IAttentionAggregationService, AttentionService>();
                        services.AddSingleton<IRequestSender, RequestSender>();
                        services.AddSingleton<IJavaScriptDeclarationsDocumentFactory, RenderingFactory>();
                        services.AddSingleton<IJavaScriptDeclarationsDocumentRenderer, RenderingRenderer>();
                        services.AddSingleton<IJavaScriptScriptEvaluator, ScriptEvaluator>();
                        services.AddSingleton<IWorkflowPortfolioService, PortfolioService>();
                        services.AddSingleton<IWorkflowRunHealthService, RunHealthService>();
                    });
                    webHost.Configure(app =>
                    {
                        app.Use(async (context, next) =>
                        {
                            context.TraceIdentifier = "wave1-trace";
                            await next();
                        });
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            ApiCapabilitiesApi.MapApiCapabilitiesApi(endpoints);
                            AttentionApi.MapAttentionApi(endpoints);
                            ExpressionsApi.MapExpressionsApi(endpoints);
                            JavaScriptRenderingApi.MapJavaScriptRenderingApi(endpoints);
                            JavaScriptExecutionApi.MapJavaScriptExecutionApi(endpoints);
                            WorkflowsDashboardApi.MapWorkflowsDashboardApi(endpoints);
                            endpoints.MapOpenApi();
                        });
                    });
                })
                .Build();
            await host.StartAsync();
            return new Wave1MinimalApiHost(host);
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

    private sealed class Wave1Auth(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Wave1MinimalApi";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[Wave1MinimalApiHost.IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(IdentityClaimTypes.Normalized, "v1") };
            if (identity == "wildcard")
            {
                claims.Add(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
                claims.Add(new Claim(IdentityClaimTypes.TenantId, "tenant-wave1"));
            }
            else if (identity == "no-tenant")
                claims.Add(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class CapabilityCatalog : IApiCapabilityCatalog
    {
        public Task<ApiCapabilitiesDocument> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApiCapabilitiesDocument([new ApiCapabilityView("wave1", "1", [new ApiCapabilityLinkView("self", "capabilities")])]));
    }

    private sealed class AttentionService : IAttentionAggregationService
    {
        public Task<AttentionAggregationResult> AggregateAsync(AttentionQuery query, CancellationToken cancellationToken = default)
        {
            if (query.ContributorIds?.Contains(string.Empty) == true)
                return Task.FromException<AttentionAggregationResult>(new AttentionQueryException("Contributor IDs cannot be empty."));
            return Task.FromResult(new AttentionAggregationResult(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero), []));
        }
    }

    private sealed class RequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult((T)(object)(request switch
            {
                ListExpressionDescriptors => new ExpressionDescriptorsResponse([]),
                ListVariableTypeDescriptors => new VariableTypeDescriptorsResponse([]),
                _ => throw new InvalidOperationException(request.GetType().FullName)
            }));
    }

    private sealed class RenderingFactory : IJavaScriptDeclarationsDocumentFactory
    {
        public ValueTask<JavaScriptDeclarationsDocument> Create(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new JavaScriptDeclarationsDocument());
    }

    private sealed class RenderingRenderer : IJavaScriptDeclarationsDocumentRenderer
    {
        public string Render(JavaScriptDeclarationsDocument typeDocument) => "{}";
    }

    private sealed class ScriptEvaluator : IJavaScriptScriptEvaluator
    {
        public ValueTask<JsonElement?> EvaluateAsync(JavaScriptScriptEvaluationRequest request) =>
            ValueTask.FromResult<JsonElement?>(JsonDocument.Parse("1").RootElement.Clone());
    }

    private sealed class PortfolioService : IWorkflowPortfolioService
    {
        public ValueTask<WorkflowPortfolioSnapshot> QueryAsync(string tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowPortfolioSnapshot(WorkflowPortfolioStatus.Ready,
                new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero), 1, 1, 0, 0));
    }

    private sealed class RunHealthService : IWorkflowRunHealthService
    {
        public ValueTask<WorkflowRunHealthSnapshot> QueryAsync(WorkflowRunHealthQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowRunHealthSnapshot(
                WorkflowRunHealthStatus.Ready,
                new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
                query.From, query.To, query.TimeZone, query.Bucket, query.IncludeTestRuns,
                StartedCount: 1, SucceededCount: 1, FailedCount: 0, CancelledCount: 0,
                IncompleteCount: 0, IncidentBearingRunCount: 0, IncidentCount: 0, RunningCount: 0,
                FailurePercentage: 0m, IncidentBearingPercentage: 0m, Buckets: [], HighestFailureDefinitions: []));
    }
}

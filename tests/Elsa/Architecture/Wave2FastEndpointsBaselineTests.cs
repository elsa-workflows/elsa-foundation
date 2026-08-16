using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Api.FastEndpoints;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Modularity.Api;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Workflows.ExecutionEvidence;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.ExecutionEvidence.Services;
using Elsa3.Activities.Design.Import;
using Elsa3.Activities.Design.Import.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using CShells;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Xunit;
using Elsa3.Models;
using Elsa.Modularity.Core.Exceptions;

namespace Elsa.Architecture.Tests;

/// <summary>Captures the immutable Wave 2 FastEndpoints-before route and OpenAPI surface.</summary>
public sealed class Wave2FastEndpointsBaselineTests : IAsyncLifetime
{
    private const string IdentityHeader = "X-Wave2-Identity";
    private const string BpmnXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="wave2-definitions" targetNamespace="urn:wave2">
          <process id="wave2-process" isExecutable="true">
            <startEvent id="wave2-start" />
            <task id="wave2-task" name="Wave 2 task" />
            <endEvent id="wave2-end" />
            <sequenceFlow id="wave2-flow-1" sourceRef="wave2-start" targetRef="wave2-task" />
            <sequenceFlow id="wave2-flow-2" sourceRef="wave2-task" targetRef="wave2-end" />
          </process>
        </definitions>
        """;

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(new ShellSettings(new ShellId("wave2-baseline")));
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "none" });
                    services.AddAuthentication(o => o.DefaultAuthenticateScheme = "none")
                        .AddScheme<AuthenticationSchemeOptions, NoopAuthenticationHandler>("none", _ => { });
                    services.AddAuthorization();
                    services.AddOpenApi();
                    new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
                    services.AddScoped<IFeatureManagementService, StubFeatureManagementService>();
                    services.AddPermissionContributor<ModuleManagementPermissionContributor>();
                    new WorkflowsExecutionEvidenceFeature().ConfigureServices(services);
                    services.AddSingleton<IExecutionEvidenceStore, BaselineEvidenceStore>();
                    services.AddOptions<ReusableActivityImportOptions>();
                    services.AddScoped<IReusableActivityImportOperationService, StubImportService>();
                    services.AddPermissionContributor<Elsa3ImportPermissionContributor>();
                    services.AddFastEndpoints(options =>
                    {
                        options.DisableAutoDiscovery = true;
                        options.Assemblies =
                        [
                            typeof(ActivitiesBpmnInterchangeFeature).Assembly,
                            typeof(ModularityApiFeature).Assembly,
                            typeof(WorkflowsExecutionEvidenceFeature).Assembly,
                            typeof(Elsa3ImportActivitiesFeature).Assembly
                        ];
                        var endpointNames = new HashSet<string>(StringComparer.Ordinal)
                        {
                            "Elsa.Activities.Bpmn.Interchange.Endpoints.AnalyzeBpmnDocumentEndpoint",
                            "Elsa.Activities.Bpmn.Interchange.Endpoints.ImportBpmnDocumentEndpoint",
                            "Elsa.Activities.Bpmn.Interchange.Endpoints.ExportBpmnDocumentEndpoint",
                            "Elsa.Modularity.Api.Endpoints.List",
                            "Elsa.Modularity.Api.Endpoints.Apply",
                            "Elsa.Workflows.ExecutionEvidence.Endpoints.GetWorkflowEvidence",
                            "Elsa.Workflows.ExecutionEvidence.Endpoints.GetCorrelatedEvidence",
                            "Elsa.Workflows.ExecutionEvidence.Endpoints.DeleteEvidence",
                            "Elsa3.Activities.Design.Import.Endpoints.UploadReusableActivityCollectionEndpoint",
                            "Elsa3.Activities.Design.Import.Endpoints.AnalyzeReusableActivityCollectionEndpoint",
                            "Elsa3.Activities.Design.Import.Endpoints.ExpandReusableActivityImportSelectionEndpoint",
                            "Elsa3.Activities.Design.Import.Endpoints.ApplyReusableActivityImportEndpoint",
                            "Elsa3.Activities.Design.Import.Endpoints.GetReusableActivityImportStatusEndpoint"
                        };
                        options.Filter = type => type.FullName is not null && endpointNames.Contains(type.FullName);
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints();
                        endpoints.MapOpenApi();
                    });
                });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public void Captures_exactly_thirteen_wave_two_fastendpoints_routes()
    {
        var endpoints = _host.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Any() == true
                && endpoint.Metadata.GetMetadata<IRouteDiagnosticsMetadata>()?.Route?.TrimStart('/') is
                    "interchange/bpmn/analyze" or "interchange/bpmn/import" or "interchange/bpmn/export"
                    or "modularity/features" or "modularity/features/apply"
                    or "_elsa/execution-evidence" or "_elsa/execution-evidence/workflows/{workflowExecutionId}"
                    or "migration/elsa3/reusable-activities/collections"
                    or "migration/elsa3/reusable-activities/collections/{collectionHandle}/analysis"
                    or "migration/elsa3/reusable-activities/collections/{collectionHandle}/selection"
                    or "migration/elsa3/reusable-activities/collections/{collectionHandle}/apply"
                    or "migration/elsa3/reusable-activities/imports/{idempotencyKey}")
            .ToArray();
        var dataSources = new[] { new SelectedEndpointDataSource(endpoints) };
        var manifest = EndpointManifestBuilder.Capture(dataSources);
        var identities = manifest.Entries.SelectMany(entry => entry.Identities)
            .Select(identity => identity.ToString())
            .Where(identity => identity.Contains("bpmn", StringComparison.OrdinalIgnoreCase)
                || identity.Contains("modularity", StringComparison.OrdinalIgnoreCase)
                || identity.Contains("execution-evidence", StringComparison.OrdinalIgnoreCase)
                || identity.Contains("elsa3", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();


        Assert.Equal(13, identities.Length);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_by_the_legacy_surface()
    {
        foreach (var request in new[]
        {
            new HttpRequestMessage(HttpMethod.Post, "/interchange/bpmn/analyze"),
            new HttpRequestMessage(HttpMethod.Post, "/interchange/bpmn/import"),
            new HttpRequestMessage(HttpMethod.Post, "/interchange/bpmn/export"),
            new HttpRequestMessage(HttpMethod.Get, "/modularity/features"),
            new HttpRequestMessage(HttpMethod.Post, "/modularity/features/apply"),
            new HttpRequestMessage(HttpMethod.Get, "/_elsa/execution-evidence?correlationId=x"),
            new HttpRequestMessage(HttpMethod.Get, "/_elsa/execution-evidence/workflows/x"),
            new HttpRequestMessage(HttpMethod.Delete, "/_elsa/execution-evidence?workflowExecutionId=x"),
            new HttpRequestMessage(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections"),
            new HttpRequestMessage(HttpMethod.Get, "/migration/elsa3/reusable-activities/collections/x/analysis"),
            new HttpRequestMessage(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections/x/selection"),
            new HttpRequestMessage(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections/x/apply"),
            new HttpRequestMessage(HttpMethod.Get, "/migration/elsa3/reusable-activities/imports/x")
        })
        {
            using (request)
            using (var response = await _client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Captures_immutable_fastendpoints_before_http_and_openapi_evidence()
    {
        var importRequest = AuthenticatedJson(HttpMethod.Post, "/interchange/bpmn/import", new
        {
            xml = BpmnXml,
            processId = "wave2-process",
            nodeIdPrefix = "wave2"
        });
        using var importResponse = await _client.SendAsync(importRequest);
        importResponse.EnsureSuccessStatusCode();
        using var importDocument = JsonDocument.Parse(await importResponse.Content.ReadAsStringAsync());
        var processNode = importDocument.RootElement.GetProperty("processNode").GetRawText();

        var cases = new List<HttpCompatibilityCase>
        {
            Anonymous(HttpMethod.Post, "/interchange/bpmn/analyze", "anonymous"),
            new(new EndpointIdentity("/interchange/bpmn/analyze", "POST"), "wildcard-valid", () =>
                AuthenticatedJson(HttpMethod.Post, "/interchange/bpmn/analyze", new { xml = BpmnXml, processId = "wave2-process" })),
            new(new EndpointIdentity("/interchange/bpmn/analyze", "POST"), "wildcard-invalid-xml", () =>
                AuthenticatedJson(HttpMethod.Post, "/interchange/bpmn/analyze", new { xml = "<not-bpmn>", processId = "wave2-process" })),
            new(new EndpointIdentity("/interchange/bpmn/import", "POST"), "wildcard-valid", () =>
                AuthenticatedJson(HttpMethod.Post, "/interchange/bpmn/import", new { xml = BpmnXml, processId = "wave2-process", nodeIdPrefix = "wave2" })),
            new(new EndpointIdentity("/interchange/bpmn/export", "POST"), "wildcard-valid", () =>
                AuthenticatedJson(HttpMethod.Post, "/interchange/bpmn/export", new { processNode = JsonDocument.Parse(processNode).RootElement, processId = "wave2-process" })),
            Anonymous(HttpMethod.Get, "/modularity/features", "anonymous"),
            new(new EndpointIdentity("/modularity/features", "GET"), "wildcard-valid", () => Authenticated(HttpMethod.Get, "/modularity/features")),
            new(new EndpointIdentity("/modularity/features/apply", "POST"), "wildcard-valid", () =>
                AuthenticatedJson(HttpMethod.Post, "/modularity/features/apply", new { revision = "baseline", features = Array.Empty<object>() })),
            new(new EndpointIdentity("/modularity/features/apply", "POST"), "wildcard-conflict", () =>
                AuthenticatedJson(HttpMethod.Post, "/modularity/features/apply", new { revision = "conflict", features = Array.Empty<object>() })),
            new(new EndpointIdentity("/_elsa/execution-evidence", "GET"), "anonymous", () => new HttpRequestMessage(HttpMethod.Get, "/_elsa/execution-evidence?correlationId=wave2")),
            new(new EndpointIdentity("/_elsa/execution-evidence", "GET"), "wildcard-correlation-page", () => Authenticated(HttpMethod.Get, "/_elsa/execution-evidence?correlationId=wave2&after=0&waitMs=1")),
            new(new EndpointIdentity("/_elsa/execution-evidence/workflows/{param}", "GET"), "wildcard-workflow-page", () => Authenticated(HttpMethod.Get, "/_elsa/execution-evidence/workflows/wave2-workflow?after=0&waitMs=1")),
            new(new EndpointIdentity("/_elsa/execution-evidence", "DELETE"), "wildcard-workflow", () => Authenticated(HttpMethod.Delete, "/_elsa/execution-evidence?workflowExecutionId=wave2-workflow")),
            new(new EndpointIdentity("/_elsa/execution-evidence", "DELETE"), "wildcard-all", () => Authenticated(HttpMethod.Delete, "/_elsa/execution-evidence?all=true")),
            new(new EndpointIdentity("/_elsa/execution-evidence", "GET"), "wildcard-invalid-correlation", () => Authenticated(HttpMethod.Get, "/_elsa/execution-evidence?correlationId=")),
            Anonymous(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections", "anonymous"),
            new(new EndpointIdentity("/migration/elsa3/reusable-activities/collections", "POST"), "wildcard-upload-multipart", () =>
                AuthenticatedMultipart(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections", "{\"definitions\":[]}")),
            new(new EndpointIdentity("/migration/elsa3/reusable-activities/collections/{param}/analysis", "GET"), "wildcard-page", () =>
                Authenticated(HttpMethod.Get, "/migration/elsa3/reusable-activities/collections/wave2-collection/analysis?offset=2&limit=1")),
            new(new EndpointIdentity("/migration/elsa3/reusable-activities/collections/{param}/selection", "POST"), "wildcard-valid", () =>
                AuthenticatedJson(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections/wave2-collection/selection", new { planId = "wave2-plan", selectedSourceVersionIds = new[] { "source-v1" } })),
            new(new EndpointIdentity("/migration/elsa3/reusable-activities/collections/{param}/selection", "POST"), "wildcard-validation-error", () =>
                AuthenticatedJson(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections/wave2-collection/selection", new { planId = "bad", selectedSourceVersionIds = new[] { "source-v1" } })),
            new(new EndpointIdentity("/migration/elsa3/reusable-activities/collections/{param}/apply", "POST"), "wildcard-applied", () =>
                AuthenticatedJson(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections/wave2-collection/apply", new { planId = "wave2-plan", selectedSourceVersionIds = new[] { "source-v1" }, idempotencyKey = "wave2-idempotency" })),
            new(new EndpointIdentity("/migration/elsa3/reusable-activities/imports/{param}", "GET"), "wildcard-status", () =>
                Authenticated(HttpMethod.Get, "/migration/elsa3/reusable-activities/imports/wave2-idempotency"))
        };

        var observations = new List<HttpCompatibilityObservation>(cases.Count);
        foreach (var testCase in cases)
            observations.Add(await HttpEvidenceCapture.CaptureAsync(_client, testCase));

        using var openApiResponse = await _client.GetAsync("/openapi/v1.json");
        openApiResponse.EnsureSuccessStatusCode();
        var openApi = OpenApiEvidenceCapture.Capture(await openApiResponse.Content.ReadAsStringAsync());
        Console.WriteLine("WAVE2_HTTP_BEFORE=" + CompatibilityJson.Serialize(observations));
        Console.WriteLine("WAVE2_OPENAPI_BEFORE=" + CompatibilityJson.Serialize(openApi));
    }

    private static HttpCompatibilityCase Anonymous(HttpMethod method, string path, string caseName) =>
        new(new EndpointIdentity(path, method.Method), caseName, () => new HttpRequestMessage(method, path));

    private static HttpRequestMessage Authenticated(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(IdentityHeader, "wildcard|tenant-wave2|user-wave2");
        return request;
    }

    private static HttpRequestMessage AuthenticatedJson(HttpMethod method, string path, object body)
    {
        var request = Authenticated(method, path);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    private static HttpRequestMessage AuthenticatedMultipart(HttpMethod method, string path, string payload)
    {
        var request = Authenticated(method, path);
        var content = new MultipartFormDataContent("wave2-boundary");
        content.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "collection");
        request.Content = content;
        return request;
    }

    private sealed class NoopAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(IdentityHeader, out var values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var parts = values.ToString().Split('|', StringSplitOptions.TrimEntries);
            var permission = parts.ElementAtOrDefault(0);
            var tenant = parts.ElementAtOrDefault(1) ?? "tenant-wave2";
            var user = parts.ElementAtOrDefault(2) ?? "user-wave2";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user),
                new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.Provider, "wave2-baseline"),
                new(IdentityClaimTypes.TenantId, tenant)
            };
            if (string.Equals(permission, "wildcard", StringComparison.OrdinalIgnoreCase))
                claims.Add(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));

            var identity = new ClaimsIdentity(claims, "none");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "none")));
        }
    }

    private sealed class StubFeatureManagementService : IFeatureManagementService
    {
        public Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureCatalogResponse("baseline", []));

        public Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default) =>
            string.Equals(request.Revision, "conflict", StringComparison.Ordinal)
                ? Task.FromException<FeatureApplyResult>(new FeatureCatalogRevisionConflictException("conflict", "baseline"))
                : Task.FromResult(new FeatureApplyResult(new FeatureCatalogResponse(request.Revision, []), 0, 0));
    }

    private sealed class StubImportService : IReusableActivityImportOperationService
    {
        public ValueTask<ReusableActivityImportUploadResult> UploadAsync(
            Stream json,
            long? contentLength,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReusableActivityImportUploadResult(
                "wave2-collection", FixedNow, FixedNow.AddHours(1), 1, contentLength ?? 21));

        public ValueTask<ReusableActivityImportAnalysisPage> AnalyzeAsync(
            string collectionHandle,
            int offset,
            int limit,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReusableActivityImportAnalysisPage(
                collectionHandle, "wave2-plan", offset, limit, 1, 3, 1, 1,
                offset + 1 >= 3, offset + 1 < 3 ? offset + 1 : null, [ImportItem], [Diagnostic]));

        public ValueTask<ReusableActivityImportSelectionReadiness> ExpandSelectionAsync(
            string collectionHandle,
            string planId,
            IReadOnlyCollection<string> selectedSourceVersionIds,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default) =>
            string.Equals(planId, "bad", StringComparison.Ordinal)
                ? ValueTask.FromException<ReusableActivityImportSelectionReadiness>(new ReusableActivityImportValidationException(
                    "The reviewed import plan is invalid.", [Diagnostic]))
                : ValueTask.FromResult(new ReusableActivityImportSelectionReadiness(
                    collectionHandle, planId, selectedSourceVersionIds.ToArray(), ["source-v1"], [], true, []));

        public ValueTask<ReusableActivityImportReceipt> ApplyAsync(
            string collectionHandle,
            string planId,
            IReadOnlyCollection<string> selectedSourceVersionIds,
            string idempotencyKey,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Receipt);

        public ValueTask<ReusableActivityImportReceipt> GetStatusAsync(
            string idempotencyKey,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Receipt);

        private static DateTimeOffset FixedNow => new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        private static Elsa3MigrationDiagnostic Diagnostic => new(
            Elsa3MigrationDiagnosticSeverity.Warning,
            "ELS3-WAVE2-DIAGNOSTIC",
            "Wave 2 deterministic diagnostic.");

        private static ReusableActivityImportItem ImportItem => new(
            "source-definition", "source-v1", 1, "fingerprint-wave2", true,
            "workflow-definition", "workflow-version", "activity-definition", "activity-version", "Wave2.Activity",
            [], [], [], [Diagnostic]);

        private static ReusableActivityImportReceipt Receipt => new(
            "receipt-wave2", "wave2-collection", "wave2-plan", "wave2-idempotency", "selection-fingerprint-wave2",
            new ReusableActivityImportAccessScope("tenant-wave2", "user-wave2"),
            ReusableActivityImportReceiptStatus.Applied, FixedNow,
            [new ReusableActivityImportSourceReceipt(
                "source-definition", "source-v1", "workflow-definition", "workflow-version",
                ReusableActivityImportResourceDisposition.Created, "workflow-navigation-wave2",
                "activity-definition", "activity-version", ReusableActivityImportResourceDisposition.Created,
                ReusableActivityImportResourceDisposition.Created, "activity-navigation-wave2", "version-navigation-wave2")]);
    }

    private sealed class BaselineEvidenceStore : IExecutionEvidenceStore
    {
        private static DateTimeOffset FixedNow => new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        private static readonly ExecutionEvidenceRecord[] Records =
        [
            new()
            {
                Sequence = 1, WorkflowExecutionId = "wave2-workflow", Kind = "WorkflowStarted", CheckpointId = "checkpoint-wave2",
                OccurredAt = FixedNow, CorrelationId = "wave2", Status = "Running"
            },
            new()
            {
                Sequence = 2, WorkflowExecutionId = "wave2-child", Kind = "Activity", CheckpointId = "checkpoint-child",
                OccurredAt = FixedNow.AddSeconds(1), CorrelationId = "wave2", ActivityType = "Wave2.Activity", Status = "Completed"
            }
        ];

        public void Append(ExecutionEvidenceCommitBatch batch) { }

        public ExecutionEvidencePage List(string workflowExecutionId, long afterSequence) =>
            Page(Records.Where(record => record.WorkflowExecutionId == workflowExecutionId && record.Sequence > afterSequence));

        public ExecutionEvidencePage ListByCorrelation(string correlationId, long afterSequence) =>
            Page(Records.Where(record => record.CorrelationId == correlationId && record.Sequence > afterSequence));

        public void Clear(string? workflowExecutionId) { }

        private static ExecutionEvidencePage Page(IEnumerable<ExecutionEvidenceRecord> records)
        {
            var result = records.ToArray();
            return new ExecutionEvidencePage(result, result.Length == 0 ? 0 : result[0].Sequence,
                result.Length == 0 ? 0 : result[^1].Sequence, result.Length > 0, result.Length == 0 ? 0 : 1);
        }
    }

    private sealed class SelectedEndpointDataSource(IReadOnlyList<Microsoft.AspNetCore.Http.Endpoint> endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Microsoft.AspNetCore.Http.Endpoint> Endpoints { get; } = endpoints;

        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}

using CShells;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Modularity.Api;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
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
using Elsa3.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Compares the migrated Wave 2 Minimal API surface with immutable before evidence.</summary>
public sealed class Wave2MinimalApiCompatibilityTests : IAsyncLifetime
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
                    // Modularity and Elsa 3 Import are wired from stubs here rather than through
                    // their features, so their owner-keyed failure services are registered directly.
                    services.AddKeyedSingleton<Elsa.Api.AspNetCore.IEndpointProblemWriter, Elsa.Modularity.Api.Endpoints.ModularityProblemWriter>("Elsa.Modularity.Api");
                    services.AddKeyedSingleton<Elsa.Api.AspNetCore.IEndpointFaultRenderer, Elsa.Modularity.Api.Endpoints.ModularityFaultRenderer>("Elsa.Modularity.Api");
                    services.AddKeyedSingleton<Elsa.Api.AspNetCore.IEndpointProblemWriter, Elsa3.Activities.Design.Import.Endpoints.ReusableActivityImportProblemWriter>("Elsa3.Activities.Design.Import");
                    services.AddKeyedSingleton<Elsa.Api.AspNetCore.IEndpointFaultRenderer, Elsa3.Activities.Design.Import.Endpoints.ReusableActivityImportFaultRenderer>("Elsa3.Activities.Design.Import");
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        new ActivitiesBpmnInterchangeFeature().MapEndpoints(endpoints, null);
                        new ModularityApiFeature().MapEndpoints(endpoints, null);
                        new WorkflowsExecutionEvidenceFeature().MapEndpoints(endpoints, null);
                        new Elsa3ImportActivitiesFeature().MapEndpoints(endpoints, null);
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
    public void Captures_exactly_thirteen_migrated_wave_two_routes()
    {
        var endpoints = _host.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Where(endpoint => endpoint is RouteEndpoint route
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Any() == true
                && route.RoutePattern.RawText?.TrimStart('/') is
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
    public async Task Unauthenticated_requests_are_rejected_by_the_migrated_surface()
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
    public async Task Catalog_permissions_cover_exact_implied_wildcard_and_forbidden_paths()
    {
        var checks = new (string Permission, HttpRequestMessage Request, HttpStatusCode Expected)[]
        {
            ("bpmn-interchange.read", AuthenticatedJson(HttpMethod.Post, "/interchange/bpmn/analyze", new { xml = BpmnXml }), HttpStatusCode.OK),
            ("bpmn-interchange.manage", AuthenticatedJson(HttpMethod.Post, "/interchange/bpmn/import", new { xml = BpmnXml }), HttpStatusCode.OK),
            ("module-management.read", Authenticated(HttpMethod.Get, "/modularity/features"), HttpStatusCode.OK),
            ("module-management.manage", AuthenticatedJson(HttpMethod.Post, "/modularity/features/apply", new { revision = "baseline", features = Array.Empty<object>() }), HttpStatusCode.OK),
            ("execution-evidence.read", Authenticated(HttpMethod.Get, "/_elsa/execution-evidence?correlationId=wave2"), HttpStatusCode.OK),
            ("execution-evidence.delete", Authenticated(HttpMethod.Delete, "/_elsa/execution-evidence?workflowExecutionId=wave2-workflow"), HttpStatusCode.NoContent),
            ("execution-evidence.manage", Authenticated(HttpMethod.Delete, "/_elsa/execution-evidence?all=true"), HttpStatusCode.NoContent),
            ("elsa3-import.read", Authenticated(HttpMethod.Get, "/migration/elsa3/reusable-activities/collections/wave2-collection/analysis"), HttpStatusCode.OK),
            ("elsa3-import.manage", AuthenticatedMultipart(HttpMethod.Post, "/migration/elsa3/reusable-activities/collections", "{}"), HttpStatusCode.Created),
            ("wildcard", Authenticated(HttpMethod.Get, "/modularity/features"), HttpStatusCode.OK),
            ("unrelated.read", Authenticated(HttpMethod.Get, "/modularity/features"), HttpStatusCode.Forbidden)
        };

        foreach (var (permission, request, expected) in checks)
        {
            request.Headers.Remove(IdentityHeader);
            request.Headers.Add(IdentityHeader, permission + "|tenant-wave2|user-wave2");
            using (request)
            using (var response = await _client.SendAsync(request))
                Assert.Equal(expected, response.StatusCode);
        }

        using var impliedModule = Authenticated(HttpMethod.Get, "/modularity/features", "module-management.manage");
        using var impliedModuleResponse = await _client.SendAsync(impliedModule);
        Assert.Equal(HttpStatusCode.OK, impliedModuleResponse.StatusCode);

        using var impliedEvidence = Authenticated(HttpMethod.Get, "/_elsa/execution-evidence?correlationId=wave2", "execution-evidence.manage");
        using var impliedEvidenceResponse = await _client.SendAsync(impliedEvidence);
        Assert.Equal(HttpStatusCode.OK, impliedEvidenceResponse.StatusCode);
    }

    [Fact]
    public async Task Normalized_identity_and_tenant_scope_are_enforced()
    {
        using var normalized = Authenticated(HttpMethod.Get, "/modularity/features", "module-management.read", normalizedMarker: "v1");
        using var normalizedResponse = await _client.SendAsync(normalized);
        Assert.Equal(HttpStatusCode.OK, normalizedResponse.StatusCode);

        using var unnormalized = Authenticated(HttpMethod.Get, "/modularity/features", "module-management.read", normalizedMarker: "legacy");
        using var unnormalizedResponse = await _client.SendAsync(unnormalized);
        Assert.Equal(HttpStatusCode.Unauthorized, unnormalizedResponse.StatusCode);

        using var otherTenant = Authenticated(HttpMethod.Get, "/migration/elsa3/reusable-activities/collections/wave2-collection/analysis",
            "elsa3-import.read", tenant: "tenant-other");
        using var otherTenantResponse = await _client.SendAsync(otherTenant);
        Assert.Equal(HttpStatusCode.NotFound, otherTenantResponse.StatusCode);
    }

    [Fact]
    public async Task Migrated_surface_matches_immutable_before_http_and_openapi_evidence()
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
        var afterOpenApiRaw = await openApiResponse.Content.ReadAsStringAsync();
        var afterOpenApi = OpenApiEvidenceCapture.Capture(afterOpenApiRaw);
        var baselineDirectory = Path.Combine(AppContext.BaseDirectory, "Baselines");
        var beforeHttp = BaselineFile.Load<HttpCompatibilityObservation[]>(
            Path.Combine(baselineDirectory, "wave2-http-fastendpoints.json"));
        var beforeOpenApi = LoadOpenApiBaseline(
            Path.Combine(baselineDirectory, "wave2-openapi-fastendpoints.json"));

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = beforeHttp, OpenApi = beforeOpenApi },
            new CompatibilityEvidenceSet { Http = observations, OpenApi = afterOpenApi });
        var beforeOpenApiIdentities = BaselineFile.Load<OpenApiIdentityBaseline[]>(
            Path.Combine(baselineDirectory, "wave2-openapi-identities-fastendpoints.json"));
        Assert.Equal(
            beforeOpenApiIdentities.Select(identity => identity.Canonical).Order(StringComparer.Ordinal),
            CaptureOpenApiIdentities(afterOpenApiRaw).Select(identity => identity.Canonical).Order(StringComparer.Ordinal));
        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    private static IReadOnlyList<OpenApiIdentity> CaptureOpenApiIdentities(string document)
    {
        using var json = JsonDocument.Parse(document);
        var identities = new List<OpenApiIdentity>();
        if (!json.RootElement.TryGetProperty("paths", out var paths))
            return identities;

        foreach (var path in paths.EnumerateObject())
            foreach (var operation in path.Value.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Object))
            {
                if (!HttpMethods.Contains(operation.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                var operationId = operation.Value.TryGetProperty("operationId", out var operationIdElement)
                    ? operationIdElement.GetString() ?? string.Empty
                    : string.Empty;
                var tags = operation.Value.TryGetProperty("tags", out var tagsElement)
                    ? tagsElement.EnumerateArray().Select(tag => tag.GetString() ?? string.Empty).ToArray()
                    : [];
                identities.Add(new OpenApiIdentity(new EndpointIdentity(path.Name, operation.Name).ToString(), operationId, tags));
            }

        return identities;
    }

    private static OpenApiEvidenceDocument LoadOpenApiBaseline(string path)
    {
        var projections = BaselineFile.Load<OpenApiBaselineDocument>(path);
        return new OpenApiEvidenceDocument(projections.Operations.Select(projection => new OpenApiOperationEvidence
        {
            Endpoint = ParseEndpoint(projection.Endpoint),
            MediaTypes = projection.MediaTypes,
            Parameters = projection.Parameters,
            RequestBody = projection.RequestBody,
            Responses = projection.Responses,
            Schemas = projection.Schemas ?? "{}"
        }).ToArray());
    }

    private static EndpointIdentity ParseEndpoint(string value)
    {
        var separator = value.IndexOf(' ');
        return new EndpointIdentity(value[(separator + 1)..], value[..separator]);
    }

    private sealed record OpenApiBaselineProjection
    {
        public required string Endpoint { get; init; }
        public required string MediaTypes { get; init; }
        public required string Parameters { get; init; }
        public required string RequestBody { get; init; }
        public required string Responses { get; init; }
        public string? Schemas { get; init; }
    }

    private sealed record OpenApiBaselineDocument
    {
        public required IReadOnlyList<OpenApiBaselineProjection> Operations { get; init; }
    }

    private sealed record OpenApiIdentityBaseline
    {
        public required string Endpoint { get; init; }
        public required string OperationId { get; init; }
        public required IReadOnlyList<string> Tags { get; init; }

        public string Canonical => CompatibilityJson.Serialize(new { Endpoint, OperationId, Tags });
    }

    private sealed record OpenApiIdentity(string Endpoint, string OperationId, IReadOnlyList<string> Tags)
    {
        public string Canonical => CompatibilityJson.Serialize(new { Endpoint, OperationId, Tags });
    }

    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private static HttpCompatibilityCase Anonymous(HttpMethod method, string path, string caseName) =>
        new(new EndpointIdentity(path, method.Method), caseName, () => new HttpRequestMessage(method, path));

    private static HttpRequestMessage Authenticated(HttpMethod method, string path, string permission = "wildcard",
        string tenant = "tenant-wave2", string user = "user-wave2", string normalizedMarker = "v1")
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(IdentityHeader, $"{permission}|{tenant}|{user}|{normalizedMarker}");
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
            var permissions = (parts.ElementAtOrDefault(0) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var tenant = parts.ElementAtOrDefault(1) ?? "tenant-wave2";
            var user = parts.ElementAtOrDefault(2) ?? "user-wave2";
            var normalizedMarker = parts.ElementAtOrDefault(3) ?? "v1";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user),
                new(IdentityClaimTypes.Normalized, normalizedMarker),
                new(IdentityClaimTypes.Provider, "wave2-baseline"),
                new(IdentityClaimTypes.TenantId, tenant)
            };
            foreach (var permission in permissions)
                claims.Add(new Claim(IdentityClaimTypes.Permission,
                    string.Equals(permission, "wildcard", StringComparison.OrdinalIgnoreCase)
                        ? PermissionKey.Wildcard
                        : permission));

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
            CancellationToken cancellationToken = default)
        {
            EnsureAccess(accessScope);
            return ValueTask.FromResult(new ReusableActivityImportUploadResult(
                "wave2-collection", FixedNow, FixedNow.AddHours(1), 1, contentLength ?? 21));
        }

        public ValueTask<ReusableActivityImportAnalysisPage> AnalyzeAsync(
            string collectionHandle,
            int offset,
            int limit,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default)
        {
            EnsureAccess(accessScope);
            return ValueTask.FromResult(new ReusableActivityImportAnalysisPage(
                collectionHandle, "wave2-plan", offset, limit, 1, 3, 1, 1,
                offset + 1 >= 3, offset + 1 < 3 ? offset + 1 : null, [ImportItem], [Diagnostic]));
        }

        public ValueTask<ReusableActivityImportSelectionReadiness> ExpandSelectionAsync(
            string collectionHandle,
            string planId,
            IReadOnlyCollection<string> selectedSourceVersionIds,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default)
        {
            EnsureAccess(accessScope);
            return string.Equals(planId, "bad", StringComparison.Ordinal)
                ? ValueTask.FromException<ReusableActivityImportSelectionReadiness>(new ReusableActivityImportValidationException(
                    "The reviewed import plan is invalid.", [Diagnostic]))
                : ValueTask.FromResult(new ReusableActivityImportSelectionReadiness(
                    collectionHandle, planId, selectedSourceVersionIds.ToArray(), ["source-v1"], [], true, []));
        }

        public ValueTask<ReusableActivityImportReceipt> ApplyAsync(
            string collectionHandle,
            string planId,
            IReadOnlyCollection<string> selectedSourceVersionIds,
            string idempotencyKey,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default)
        {
            EnsureAccess(accessScope);
            return ValueTask.FromResult(Receipt);
        }

        public ValueTask<ReusableActivityImportReceipt> GetStatusAsync(
            string idempotencyKey,
            ReusableActivityImportAccessScope accessScope,
            CancellationToken cancellationToken = default)
        {
            EnsureAccess(accessScope);
            return ValueTask.FromResult(Receipt);
        }

        private static void EnsureAccess(ReusableActivityImportAccessScope accessScope)
        {
            if (!string.Equals(accessScope.TenantId, "tenant-wave2", StringComparison.Ordinal)
                || !string.Equals(accessScope.UserId, "user-wave2", StringComparison.Ordinal))
                throw new ReusableActivityImportNotFoundException("The Elsa 3 import resource was not found.");
        }

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

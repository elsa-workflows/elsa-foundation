using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class Wave9RuntimeMinimalApiCompositionTests
{
    private static readonly string BaselineDirectory = Path.Join(AppContext.BaseDirectory, "Baselines");

    [Fact]
    public async Task Runtime_minimal_mapper_publishes_all_24_routes_with_baseline_openapi_coverage()
    {
        var receipt = BaselineFile.Load<JsonElement>(Path.Join(BaselineDirectory, "wave9-runtime-before-capture-receipt.json"));
        Assert.Equal(24, receipt.GetProperty("registrationCount").GetInt32());
        Assert.Equal(24, receipt.GetProperty("operationCount").GetInt32());

        await using var host = await RuntimeHost.StartAsync();
        var openApi = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());
        Assert.Equal(24, openApi.Operations.Count);

        var expected = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave9-runtime-openapi-fastendpoints.json"));
        Assert.Equal(
            expected.Operations.Select(operation => operation.Endpoint.Method.Value + " " + Normalize(operation.Endpoint.Route.Value)).Order(StringComparer.Ordinal),
            openApi.Operations.Select(operation => operation.Endpoint.Method.Value + " " + Normalize(operation.Endpoint.Route.Value)).Order(StringComparer.Ordinal));

        var endpoints = host.Endpoints;
        Assert.Equal(24, endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi));
        Assert.All(endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi), endpoint =>
        {
            Assert.Equal("Elsa.Workflows.Runtime.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner);
            Assert.NotNull(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>());

            var ownerAssembly = typeof(WorkflowsRuntimeApi).Assembly;
            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
            var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
            if (accepts?.RequestType is { } requestType)
                Assert.NotSame(ownerAssembly, requestType.Assembly);
            Assert.DoesNotContain(produces, metadata => metadata.Type is not null && ReferenceEquals(metadata.Type.Assembly, ownerAssembly));
            Assert.DoesNotContain(endpoint.Metadata, metadata => metadata is Type type && ReferenceEquals(type.Assembly, ownerAssembly));
            Assert.DoesNotContain(endpoint.Metadata, metadata => metadata is MemberInfo member && ReferenceEquals(member.Module.Assembly, ownerAssembly));
            Assert.Equal(typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke)), endpoint.Metadata.GetMetadata<MethodInfo>());
        });
    }

    [Fact]
    public void Runtime_wire_contracts_live_in_stable_api_core_with_legacy_type_forwarders()
    {
        var coreAssembly = typeof(WorkflowInstanceListView).Assembly;
        var legacyAssembly = typeof(WorkflowsRuntimeApi).Assembly;

        Assert.Equal("Elsa.Workflows.Runtime.Api.Core", coreAssembly.GetName().Name);
        Assert.Same(coreAssembly, typeof(ExecuteWorkflow).Assembly);
        Assert.Same(coreAssembly, typeof(ActivityExecutionValuePayloadView).Assembly);

        var runtimeAssembly = typeof(RuntimeWorkflowOutputStateProjection).Assembly;
        Assert.Equal("Elsa.Workflows.Runtime.Core", typeof(WorkflowOutputProjection).Assembly.GetName().Name);
        Assert.Contains(typeof(WorkflowOutputProjection), runtimeAssembly.GetForwardedTypes());
        Assert.NotNull(typeof(WorkflowOutputView).GetMethod(nameof(WorkflowOutputView.From), [typeof(WorkflowOutputProjection)]));

        var forwardedTypes = legacyAssembly.GetForwardedTypes();
        Assert.Contains(typeof(WorkflowInstanceListView), forwardedTypes);
        Assert.Contains(typeof(ExecuteWorkflow), forwardedTypes);
        Assert.Contains(typeof(ActivityExecutionValuePayloadView), forwardedTypes);
    }

    [Fact]
    public async Task Runtime_openapi_differences_are_consumed_by_pinned_two_sided_approvals()
    {
        var before = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave9-runtime-openapi-fastendpoints.json"));
        using var approvalDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Join(BaselineDirectory, "wave9-runtime-openapi-approved-differences.json")));
        var approvals = approvalDocument.RootElement.GetProperty("differences").EnumerateArray().ToArray();
        await using var host = await RuntimeHost.StartAsync();
        var after = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());
        var beforeOperations = BuildOperations(before);
        var afterOperations = BuildOperations(after);
        var approvedFacetKeys = RuntimeOpenApiApprovalValidator.Validate(beforeOperations, afterOperations, approvals);
        var consumedFacetKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in beforeOperations.Keys.Union(afterOperations.Keys, StringComparer.Ordinal))
        {
            Assert.True(beforeOperations.TryGetValue(key, out var beforeOperation), $"Missing before OpenAPI operation '{key}'.");
            Assert.True(afterOperations.TryGetValue(key, out var afterOperation), $"Missing after OpenAPI operation '{key}'.");
            var routeApprovals = approvals.Where(approval => RuntimeOpenApiApprovalValidator.EndpointKey(approval) == key).ToArray();
            foreach (var approval in routeApprovals)
            {
                RuntimeOpenApiApprovalValidator.RemoveApprovedFacets(beforeOperation!, afterOperation!, approval, consumedFacetKeys);
            }

            Assert.True(JsonNode.DeepEquals(beforeOperation, afterOperation),
                $"Unapproved OpenAPI change at {key}. Before={beforeOperation}; After={afterOperation}");
        }

        Assert.Equal(approvedFacetKeys.Order(StringComparer.Ordinal), consumedFacetKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Runtime_openapi_approval_validator_rejects_a_mutated_after_facet()
    {
        var before = BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory, "wave9-runtime-openapi-fastendpoints.json"));
        using var approvalDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Join(BaselineDirectory, "wave9-runtime-openapi-approved-differences.json")));
        var approvals = approvalDocument.RootElement.GetProperty("differences").EnumerateArray().ToArray();
        await using var host = await RuntimeHost.StartAsync();
        var after = OpenApiEvidenceCapture.Capture(await host.GetOpenApiAsync());
        var beforeOperations = BuildOperations(before);
        var afterOperations = BuildOperations(after);
        var approval = approvals[0];
        var key = RuntimeOpenApiApprovalValidator.EndpointKey(approval);
        afterOperations[key]["requestBody"] = "{\"mutated\":true}";

        var exception = Assert.Throws<RuntimeOpenApiApprovalValidationException>(() =>
            RuntimeOpenApiApprovalValidator.Validate(beforeOperations, afterOperations, approvals));
        Assert.Equal($"route:{key}", exception.Key);
        Assert.Equal($"route:{key}: after requestBody value does not match document.", exception.Message);
    }

    [Fact]
    public void Runtime_openapi_approval_validator_rejects_malformed_facets_with_typed_failures()
    {
        var before = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
        {
            ["GET /sample"] = Operation("GET /sample", "[]", "{}", "{}", "[]")
        };
        var after = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
        {
            ["GET /sample"] = Operation("GET /sample", "[\"application/json\"]", "{\"request\":true}", "{}", "[]")
        };

        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[\"application/json\"]", "[\"application/json\"]")]),
            "route:GET /sample", "mediaTypes before and after values must differ.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[]", "[\"application/json\"]", ("extra", JsonValue.Create(true)))]),
            "route:GET /sample", "Unknown approval property 'extra'.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[]", null)]),
            "route:GET /sample", "Approval facet requires both beforeMediaTypes and afterMediaTypes.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[]", "[\"application/json\"]"), Approval("GET", "/sample", "[]", "[\"application/json\"]")]),
            "route:GET /sample:mediaTypes", "Duplicate approval facet.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/stale", "[]", "[\"application/json\"]")]),
            "route:GET /stale", "Route is absent from the before document.");
        AssertApprovalFailure(() => RuntimeOpenApiApprovalValidator.Validate(before, after, [Approval("GET", "/sample", "[\"wrong\"]", "[\"application/json\"]")]),
            "route:GET /sample", "before mediaTypes value does not match document.");
    }

    [Fact]
    public async Task Every_runtime_route_challenges_anonymous_callers()
    {
        await using var host = await RuntimeHost.StartAsync();
        var runtimeEndpoints = host.Endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi).ToArray();
        Assert.Equal(24, runtimeEndpoints.Length);
        foreach (var endpoint in runtimeEndpoints)
        {
            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single() ?? HttpMethods.Get;
            var path = endpoint is RouteEndpoint routeEndpoint
                ? System.Text.RegularExpressions.Regex.Replace(routeEndpoint.RoutePattern.RawText ?? string.Empty, "\\{[^}]+\\}", "sample")
                : throw new Xunit.Sdk.XunitException("Runtime endpoint is missing a route pattern.");
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT")
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Every_runtime_route_forbids_authenticated_callers_without_the_catalog_action()
    {
        await using var host = await RuntimeHost.StartAsync();
        var runtimeEndpoints = host.Endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi).ToArray();
        Assert.Equal(24, runtimeEndpoints.Length);
        foreach (var endpoint in runtimeEndpoints)
        {
            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single() ?? HttpMethods.Get;
            var path = endpoint is RouteEndpoint routeEndpoint
                ? System.Text.RegularExpressions.Regex.Replace(routeEndpoint.RoutePattern.RawText ?? string.Empty, "\\{[^}]+\\}", "sample")
                : throw new Xunit.Sdk.XunitException("Runtime endpoint is missing a route pattern.");
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.TryAddWithoutValidation("X-Wave9-Authenticated", "true");
            if (method is "POST" or "PUT")
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public void Runtime_baseline_receipt_is_pinned_and_comparer_detects_http_mutations()
    {
        var receiptPath = Path.Join(BaselineDirectory, "wave9-runtime-before-capture-receipt.json");
        var httpPath = Path.Join(BaselineDirectory, "wave9-runtime-http-fastendpoints.json");
        var receipt = BaselineFile.Load<JsonElement>(receiptPath);
        var observations = BaselineFile.Load<HttpCompatibilityObservation[]>(httpPath);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(httpPath))).ToLowerInvariant();
        Assert.Equal(receipt.GetProperty("httpSha256").GetString(), hash);
        Assert.Equal(receipt.GetProperty("caseCount").GetInt32(), observations.Length);
        Assert.Equal(24, observations.Count(observation => observation.StatusCode == StatusCodes.Status401Unauthorized));
        Assert.Equal(24, observations.Count(observation => observation.Case.EndsWith("|trusted-success", StringComparison.Ordinal)));
        Assert.Equal(24, observations.Select(observation => observation.Endpoint.ToString()).Distinct(StringComparer.Ordinal).Count());

        var before = new CompatibilityEvidenceSet { Http = observations };
        var mutated = observations.Select(observation => observation.Case == "execute|trusted-success"
                ? observation with { StatusCode = StatusCodes.Status201Created }
                : observation).ToArray();
        var result = CompatibilityComparer.Compare(before, new CompatibilityEvidenceSet { Http = mutated });
        var delta = Assert.Single(result.Deltas);
        Assert.Equal(CompatibilityFacet.Status, delta.Facet);
        Assert.False(result.IsCompatible);
    }

    [Fact]
    public async Task Runtime_minimal_host_replays_every_frozen_http_case_against_the_mapped_routes()
    {
        var baseline = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory, "wave9-runtime-http-fastendpoints.json"));
        await using var host = await RuntimeHost.StartAsync();
        var after = (await Task.WhenAll(RuntimeReplayCases().Select(testCase => HttpEvidenceCapture.CaptureAsync(host.Client, testCase))))
            .Select(NormalizeTraceIds)
            .ToArray();

        var comparison = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = baseline },
            new CompatibilityEvidenceSet { Http = after });
        Assert.True(comparison.IsCompatible, string.Join(Environment.NewLine, comparison.Failures));
        Assert.Equal(baseline.Length, after.Length);
        Assert.Equal(baseline.Select(x => x.Case).Order(StringComparer.Ordinal), after.Select(x => x.Case).Order(StringComparer.Ordinal));
    }

    private static string Normalize(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, "\\{[^}]+\\}", "{param}");

    private static HttpCompatibilityObservation NormalizeTraceIds(HttpCompatibilityObservation observation)
    {
        static string Normalize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;
            var node = JsonNode.Parse(json);
            if (node is not JsonObject document || !document.TryGetPropertyValue("traceId", out var traceId))
                return json;
            if (traceId is null || traceId.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(traceId.GetValue<string>()))
                throw new InvalidDataException("ProblemDetails traceId must be a non-empty JSON string.");
            document["traceId"] = "capture-trace-id";
            return CompatibilityJson.Canonicalize(document);
        }

        if (!observation.Headers.TryGetValue("x-runtime-capture-binding", out var binding))
            return observation with
            {
                Body = Normalize(observation.Body),
                Json = Normalize(observation.Json),
                ProblemDetails = Normalize(observation.ProblemDetails)
            };

        var headers = new SortedDictionary<string, string>(observation.Headers.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        headers.Remove("x-runtime-capture-binding");
        return observation with
        {
            Body = Normalize(observation.Body),
            Json = Normalize(observation.Json),
            ProblemDetails = Normalize(observation.ProblemDetails),
            Binding = binding,
            Headers = headers
        };
    }

    private static IReadOnlyList<HttpCompatibilityCase> RuntimeReplayCases()
    {
        var routes = new (HttpMethod Method, string Name, string Path, string Template)[]
        {
            (HttpMethod.Get, "get-instance", "/runtime/workflows/instances/sample", "/runtime/workflows/instances/{workflowExecutionId}"),
            (HttpMethod.Get, "list-instances", "/runtime/workflows/instances?status=Completed&definitionId=sample-definition&correlationId=sample-correlation&take=2&cursor=next&workflowExecutionId=sample-execution&artifactId=sample-artifact&from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z&runKind=Root", "/runtime/workflows/instances"),
            (HttpMethod.Get, "list-instances-page", "/runtime/workflows/instances/page?status=Completed&definitionId=sample-definition&take=2&cursor=next", "/runtime/workflows/instances/page"),
            (HttpMethod.Get, "list-executables", "/runtime/workflows/executables?scope=All&includeRetired=true", "/runtime/workflows/executables"),
            (HttpMethod.Get, "get-executable", "/runtime/workflows/executables/sample", "/runtime/workflows/executables/{artifactId}"),
            (HttpMethod.Get, "get-executable-input-sources", "/runtime/workflows/executables/sample/source-references/source/input-sources", "/runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources"),
            (HttpMethod.Get, "get-executable-provenance", "/runtime/workflows/executables/sample/provenance", "/runtime/workflows/executables/{artifactId}/provenance"),
            (HttpMethod.Post, "execute", "/runtime/workflows/executables/sample/execute", "/runtime/workflows/executables/{artifactId}/execute"),
            (HttpMethod.Post, "dispatch-stimulus", "/runtime/workflows/stimuli", "/runtime/workflows/stimuli"),
            (HttpMethod.Get, "list-dispatches", "/runtime/workflows/dispatches?parentWorkflowExecutionId=parent&childWorkflowExecutionId=child&status=Completed&take=2&afterCreatedAt=2026-01-01T00:00:00Z&afterDispatchId=after", "/runtime/workflows/dispatches"),
            (HttpMethod.Get, "get-dispatch", "/runtime/workflows/dispatches/sample", "/runtime/workflows/dispatches/{dispatchId}"),
            (HttpMethod.Post, "redrive-dispatch", "/runtime/workflows/dispatches/sample/redrive", "/runtime/workflows/dispatches/{dispatchId}/redrive"),
            (HttpMethod.Get, "get-activity-execution", "/runtime/workflows/instances/sample/activity-executions/activity", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}"),
            (HttpMethod.Get, "get-activity-descendants", "/runtime/workflows/instances/sample/activity-executions/activity/descendants?cursor=cursor&limit=2&include=incidents", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants"),
            (HttpMethod.Get, "get-activity-layout", "/runtime/workflows/instances/sample/activity-executions/activity/layout", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout"),
            (HttpMethod.Get, "get-activity-value-payload", "/runtime/workflows/instances/sample/activity-executions/activity/value-evidence/evidence/payload", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload"),
            (HttpMethod.Get, "list-incidents", "/runtime/workflows/instances/sample/incidents?blockingOnly=true", "/runtime/workflows/instances/{workflowExecutionId}/incidents"),
            (HttpMethod.Get, "get-runtime-diagnostics", "/runtime/workflows/diagnostics/settings?scope=runtime", "/runtime/workflows/diagnostics/settings"),
            (HttpMethod.Put, "save-runtime-diagnostics", "/runtime/workflows/diagnostics/settings", "/runtime/workflows/diagnostics/settings"),
            (HttpMethod.Post, "submit-alteration-plan", "/runtime/workflows/alteration-plans", "/runtime/workflows/alteration-plans"),
            (HttpMethod.Get, "get-alteration-plan", "/runtime/workflows/alteration-plans/sample", "/runtime/workflows/alteration-plans/{planId}"),
            (HttpMethod.Get, "page-alteration-jobs", "/runtime/workflows/alteration-plans/sample/jobs/page?take=2&cursor=cursor", "/runtime/workflows/alteration-plans/{planId}/jobs/page"),
            (HttpMethod.Get, "get-alteration-job", "/runtime/workflows/alteration-plans/sample/jobs/job", "/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}"),
            (HttpMethod.Post, "cancel-alteration-plan", "/runtime/workflows/alteration-plans/sample/cancel", "/runtime/workflows/alteration-plans/{planId}/cancel")
        };

        var cases = routes.Select(route => Create(route.Method, route.Name, route.Path, route.Template, null)).ToList();
        cases.AddRange(routes.Select(route => Create(route.Method, route.Name + "|trusted-success", route.Path, route.Template, "trusted",
            route.Method == HttpMethod.Get ? null : route.Name == "submit-alteration-plan" ? "{\"target\":null,\"alterations\":[]}" : "{}")));
        cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name is "execute" or "redrive-dispatch").Select(route =>
            Create(route.Method, route.Name + "|trusted-route-body-precedence", route.Path.Replace("sample", "route-id", StringComparison.Ordinal), route.Template, "trusted",
                route.Name == "execute" ? "{\"artifactId\":\"body-id\",\"sourceReferenceId\":\"body-source\"}" : "{\"dispatchId\":\"body-id\",\"requestId\":\"request-id\"}")));
        cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
            Create(route.Method, route.Name + "|trusted-malformed-json", route.Path, route.Template, "trusted", "{ malformed")));
        cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
            Create(route.Method, route.Name + "|trusted-literal-null", route.Path, route.Template, "trusted", "null", null)));
        cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
            Create(route.Method, route.Name + "|trusted-empty-body", route.Path, route.Template, "trusted", "", "application/json")));
        cases.AddRange(routes.Where(route => route.Method != HttpMethod.Get && route.Name != "cancel-alteration-plan").Select(route =>
            Create(route.Method, route.Name + "|trusted-absent-content-type", route.Path, route.Template, "trusted", "{}", null)));
        cases.Add(Create(HttpMethod.Get, "get-instance|trusted-not-found", "/runtime/workflows/instances/missing", "/runtime/workflows/instances/{workflowExecutionId}", "trusted"));
        cases.Add(Create(HttpMethod.Get, "get-activity-execution|trusted-not-found", "/runtime/workflows/instances/missing/activity-executions/missing", "/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", "trusted"));
        cases.Add(Create(HttpMethod.Get, "list-incidents|trusted-not-found", "/runtime/workflows/instances/missing/incidents", "/runtime/workflows/instances/{workflowExecutionId}/incidents", "trusted"));
        cases.Add(Create(HttpMethod.Get, "get-alteration-plan|trusted-not-found", "/runtime/workflows/alteration-plans/missing", "/runtime/workflows/alteration-plans/{planId}", "trusted"));
        cases.Add(Create(HttpMethod.Get, "page-alteration-jobs|trusted-not-found", "/runtime/workflows/alteration-plans/missing/jobs/page?take=2", "/runtime/workflows/alteration-plans/{planId}/jobs/page", "trusted"));
        cases.Add(Create(HttpMethod.Get, "page-alteration-jobs|trusted-invalid-take", "/runtime/workflows/alteration-plans/sample/jobs/page?take=999", "/runtime/workflows/alteration-plans/{planId}/jobs/page", "trusted"));
        cases.Add(Create(HttpMethod.Post, "submit-alteration-plan|trusted-missing-idempotency", "/runtime/workflows/alteration-plans", "/runtime/workflows/alteration-plans", "trusted", "{}"));
        return cases;

        static HttpCompatibilityCase Create(HttpMethod method, string name, string route, string template, string? identity, string? body = null, string? contentType = "application/json") =>
            new(new(template, method.Method), name, () =>
            {
                var request = new HttpRequestMessage(method, route);
                if (identity is not null)
                    request.Headers.TryAddWithoutValidation(RuntimeAuthentication.IdentityHeader, identity);
                if (method != HttpMethod.Get && method != HttpMethod.Delete)
                {
                    request.Content = contentType is null
                        ? new ByteArrayContent(Encoding.UTF8.GetBytes(body ?? "{}"))
                        : new StringContent(body ?? "{}", Encoding.UTF8, contentType);
                }
                if (name.StartsWith("submit-alteration-plan", StringComparison.Ordinal) && !name.Contains("missing-idempotency", StringComparison.Ordinal))
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", "capture-idempotency-key");
                return request;
            });
    }

    private static Dictionary<string, JsonObject> BuildOperations(OpenApiEvidenceDocument document) =>
        document.Operations.ToDictionary(operation => operation.Endpoint.ToString(), operation => JsonNode.Parse(operation.Canonical)!.AsObject(), StringComparer.Ordinal);

    private static JsonObject Operation(string endpoint, string mediaTypes, string requestBody, string responses, string schemas) => new()
    {
        ["endpoint"] = endpoint,
        ["mediaTypes"] = mediaTypes,
        ["parameters"] = "[]",
        ["requestBody"] = requestBody,
        ["responses"] = responses,
        ["schemas"] = schemas
    };

    private static JsonElement Approval(string method, string path, string beforeMediaTypes, string? afterMediaTypes, (string Name, JsonNode Value)? extra = null)
    {
        var node = new JsonObject
        {
            ["method"] = method,
            ["path"] = path,
            ["reason"] = "test",
            ["beforeMediaTypes"] = JsonNode.Parse(beforeMediaTypes)
        };
        if (afterMediaTypes is not null)
            node["afterMediaTypes"] = JsonNode.Parse(afterMediaTypes);
        if (extra is { } item)
            node[item.Name] = item.Value;
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static void AssertApprovalFailure(Action action, string key, string reason)
    {
        var exception = Assert.Throws<RuntimeOpenApiApprovalValidationException>(action);
        Assert.Equal(key, exception.Key);
        Assert.Equal($"{key}: {reason}", exception.Message);
    }

    private sealed class RuntimeOpenApiApprovalValidationException(string key, string reason) : Exception($"{key}: {reason}")
    {
        public string Key { get; } = key;
    }

    private static class RuntimeOpenApiApprovalValidator
    {
        private static readonly IReadOnlySet<string> AllowedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "method", "path", "reason",
            "beforeMediaTypes", "afterMediaTypes",
            "beforeRequestBody", "afterRequestBody",
            "beforeSchemas", "afterSchemas",
            "beforeResponseSchemas", "afterResponseSchemas"
        };

        private static readonly (string Before, string After, string Facet)[] Facets =
        [
            ("beforeMediaTypes", "afterMediaTypes", "mediaTypes"),
            ("beforeRequestBody", "afterRequestBody", "requestBody"),
            ("beforeSchemas", "afterSchemas", "schemas"),
            ("beforeResponseSchemas", "afterResponseSchemas", "responseSchemas")
        ];

        public static string EndpointKey(JsonElement approval) =>
            $"{approval.GetProperty("method").GetString()!.ToUpperInvariant()} {approval.GetProperty("path").GetString()!}";

        public static IReadOnlySet<string> Validate(
            IReadOnlyDictionary<string, JsonObject> beforeOperations,
            IReadOnlyDictionary<string, JsonObject> afterOperations,
            IReadOnlyList<JsonElement> approvals)
        {
            var facetKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var approval in approvals)
            {
                var method = approval.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
                var path = approval.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                var key = $"route:{method?.ToUpperInvariant() ?? "<unknown>"} {path ?? "<unknown>"}";
                Require(method is not null, key, "Approval is missing 'method'.");
                Require(path is not null, key, "Approval is missing 'path'.");
                ValidateProperties(approval, key);
                var endpointKey = $"{method!.ToUpperInvariant()} {path}";
                Require(beforeOperations.TryGetValue(endpointKey, out var beforeOperation), key, "Route is absent from the before document.");
                Require(afterOperations.TryGetValue(endpointKey, out var afterOperation), key, "Route is absent from the after document.");

                var recognized = 0;
                foreach (var (beforeName, afterName, facet) in Facets)
                {
                    var hasBefore = approval.TryGetProperty(beforeName, out var before);
                    var hasAfter = approval.TryGetProperty(afterName, out var after);
                    Require(hasBefore == hasAfter, key, $"Approval facet requires both {beforeName} and {afterName}.");
                    if (!hasBefore)
                        continue;

                    Require(!JsonElement.DeepEquals(before, after), key, $"{facet} before and after values must differ.");
                    var facetKey = $"{key}:{facet}";
                    Require(facetKeys.Add(facetKey), facetKey, "Duplicate approval facet.");
                    Require(JsonNode.DeepEquals(ParseApprovalValue(before), GetFacet(beforeOperation!, facet)), key, $"before {facet} value does not match document.");
                    Require(JsonNode.DeepEquals(ParseApprovalValue(after), GetFacet(afterOperation!, facet)), key, $"after {facet} value does not match document.");
                    recognized++;
                }

                Require(recognized > 0, key, "Approval does not declare a recognized changed facet.");
            }

            return facetKeys;
        }

        public static void RemoveApprovedFacets(JsonObject beforeOperation, JsonObject afterOperation, JsonElement approval, ISet<string> consumedFacetKeys)
        {
            var key = EndpointKey(approval);
            foreach (var (beforeName, afterName, facet) in Facets)
            {
                if (!approval.TryGetProperty(beforeName, out _))
                    continue;

                consumedFacetKeys.Add($"route:{key}:{facet}");
                if (facet == "responseSchemas")
                {
                    RemoveResponseSchemas(beforeOperation, approval.GetProperty(beforeName));
                    RemoveResponseSchemas(afterOperation, approval.GetProperty(afterName));
                }
                else
                {
                    beforeOperation.Remove(facet);
                    afterOperation.Remove(facet);
                }
            }
        }

        private static void ValidateProperties(JsonElement approval, string key)
        {
            foreach (var property in approval.EnumerateObject())
                Require(AllowedProperties.Contains(property.Name), key, $"Unknown approval property '{property.Name}'.");
        }

        private static JsonNode ParseApprovalValue(JsonElement value) => JsonNode.Parse(value.GetRawText())!;

        private static JsonNode GetFacet(JsonObject operation, string facet) =>
            facet == "responseSchemas"
                ? GetResponseSchemas(operation)
                : JsonNode.Parse(operation[facet]!.GetValue<string>())!;

        private static JsonObject GetResponseSchemas(JsonObject operation)
        {
            var responses = JsonNode.Parse(operation["responses"]!.GetValue<string>())!.AsObject();
            var result = new JsonObject();
            foreach (var (status, responseNode) in responses)
            {
                var content = responseNode?["content"]?.AsObject();
                if (content is null)
                    continue;

                var mediaTypes = new JsonObject();
                foreach (var (mediaType, mediaNode) in content)
                {
                    var schema = mediaNode?["schema"];
                    if (schema is not null)
                        mediaTypes[mediaType] = schema.DeepClone();
                }

                if (mediaTypes.Count > 0)
                    result[status] = mediaTypes;
            }

            return result;
        }

        private static void RemoveResponseSchemas(JsonObject operation, JsonElement approval)
        {
            var responses = JsonNode.Parse(operation["responses"]!.GetValue<string>())!.AsObject();
            foreach (var status in approval.EnumerateObject())
            {
                var content = responses[status.Name]?["content"]?.AsObject();
                if (content is null)
                    continue;
                foreach (var mediaType in status.Value.EnumerateObject())
                    content[mediaType.Name]?.AsObject().Remove("schema");
            }

            operation["responses"] = responses.ToJsonString();
        }

        private static void Require(bool condition, string key, string reason)
        {
            if (!condition)
                throw new RuntimeOpenApiApprovalValidationException(key, reason);
        }
    }

    private sealed class RuntimeHost(IHost host) : IAsyncDisposable
    {
        public HttpClient Client { get; } = host.GetTestClient();
        public IReadOnlyList<Endpoint> Endpoints => host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        public static async Task<RuntimeHost> StartAsync()
        {
            var host = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "Elsa.Workflows.Runtime.Api");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddDynamicEndpointApiExplorerRefresh();
                    services.AddOpenApi();
                    services.AddAuthentication(RuntimeAuthentication.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, RuntimeAuthentication>(RuntimeAuthentication.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>([RuntimeAuthentication.SchemeName], StringComparer.Ordinal));
                    services.AddHttpContextAccessor();
                    services.AddSingleton<IRequestSender, RuntimeReplayRequestSender>();
                    services.AddSingleton<ICommandSender, RuntimeReplayCommandSender>();
                    new WorkflowsRuntimeApiFeature().ConfigureServices(services);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(endpoints);
                        endpoints.MapOpenApi();
                    });
                });
            }).Build();
            await host.StartAsync();
            return new(host);
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

    private sealed class RuntimeAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Wave9Runtime";
        public const string IdentityHeader = "X-Runtime-Capture-Identity";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var replay = Request.Headers.TryGetValue(IdentityHeader, out var replayIdentity) && string.Equals(replayIdentity, "trusted", StringComparison.Ordinal);
            if (!replay && !Request.Headers.ContainsKey("X-Wave9-Authenticated"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(Scheme.Name);
            identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
            if (replay)
            {
                identity.AddClaim(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
                identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, "capture-tenant"));
            }
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class RuntimeReplayRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult(CreateResponse<T>(request, contextAccessor.HttpContext));

        private static T CreateResponse<T>(IRequest<T> request, HttpContext? context) where T : notnull
        {
            if (context is not null)
            {
                var route = string.Join(",", context.Request.RouteValues.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
                var requestValues = string.Join(",", request.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .OrderBy(x => x.Name, StringComparer.Ordinal)
                    .Select(property => $"{property.Name}={Format(property.GetValue(request))}"));
                context.Response.Headers["X-Runtime-Capture-Binding"] = $"route={route};request={requestValues}";

                if (context.Request.Path.Value?.Contains("/missing", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (typeof(T).Name is "GetWorkflowInstanceResponse" or "GetActivityExecutionResponse" or "GetActivityExecutionDescendantsResponse" or "GetActivityExecutionLayoutResponse" or "GetWorkflowDispatchResponse")
                        return (T)CreateResponseWrapper(typeof(T));
                    if (typeof(T).Name == "ListIncidentsResponse")
                        return (T)CreateNotFoundListIncidentsResponse();
                    if (typeof(T).Name.Contains("WorkflowAlteration", StringComparison.Ordinal))
                        throw new Elsa.Workflows.Runtime.Api.Handlers.Alterations.WorkflowAlterationResourceNotFoundException();
                }

                if (context.Request.Query.TryGetValue("take", out var take) && int.TryParse(take, out var requestedTake) && requestedTake > 100)
                    throw new ArgumentException("The take value must be between 1 and 100.");
            }

            return (T)CreateValue(typeof(T), typeof(T).Name, context?.Request.Path.Value?.Contains("/terminal", StringComparison.OrdinalIgnoreCase) == true);
        }

        private static object CreateResponseWrapper(Type type)
        {
            var constructor = type.GetConstructors().Single();
            return constructor.Invoke(constructor.GetParameters().Select(parameter => CreateDefault(parameter.ParameterType)).ToArray());
        }

        private static object CreateNotFoundListIncidentsResponse()
        {
            var constructor = typeof(ListIncidentsResponse).GetConstructors().Single();
            return constructor.Invoke([false, Array.Empty<IncidentStateView>(), 0]);
        }

        private static object? CreateDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        internal static object? CreateValue(Type type, string name, bool terminal = false, int depth = 0)
        {
            if (depth > 8)
                return CreateDefault(type);
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable is not null)
                return CreateValue(nullable, name, terminal, depth + 1);
            if (type == typeof(string))
                return name.Contains("Status", StringComparison.OrdinalIgnoreCase) ? "Completed" : $"capture-{name.ToLowerInvariant()}";
            if (type == typeof(DateTimeOffset))
                return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            if (type == typeof(DateTime))
                return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (type == typeof(Guid))
                return Guid.Parse("11111111-1111-1111-1111-111111111111");
            if (type == typeof(bool))
                return name is "Shed" or "IsTerminalNoOp" ? terminal && name == "IsTerminalNoOp" : name is "WorkflowExists" or "Live" or "ProtectedFromCollection" or "RedriveEligible";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                return Convert.ChangeType(1, type, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(JsonElement))
                return JsonDocument.Parse("{\"capture\":true}").RootElement.Clone();
            if (type.IsEnum)
                return Enum.GetValues(type).GetValue(0);
            if (type == typeof(object))
                return new Dictionary<string, object> { ["capture"] = true };
            if (type.IsArray)
                return Array.CreateInstance(type.GetElementType()!, 0);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments()))!;
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            if (!type.IsClass)
                return CreateDefault(type);

            var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(x => x.GetParameters().Length)
                .FirstOrDefault();
            if (constructor is null)
                return RuntimeHelpers.GetUninitializedObject(type);
            var arguments = constructor.GetParameters().Select(parameter => CreateValue(parameter.ParameterType, parameter.Name ?? name, terminal, depth + 1)).ToArray();
            try
            {
                if (type == typeof(WorkflowExecutionStartDispatchView))
                    arguments[4] = "Accepted";
                return constructor.Invoke(arguments);
            }
            catch
            {
                return RuntimeHelpers.GetUninitializedObject(type);
            }
        }

        private static string Format(object? value) => value switch
        {
            null => "<null>",
            IEnumerable values when value is not string => $"[{string.Join("|", values.Cast<object?>().Select(Format))}]",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"
        };
    }

    private sealed class RuntimeReplayCommandSender(IHttpContextAccessor contextAccessor) : ICommandSender
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult(CreateResponse<T>(command, contextAccessor.HttpContext));

        public Task Send(ICommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static T CreateResponse<T>(ICommand<T> command, HttpContext? context) where T : notnull
        {
            if (context is not null)
            {
                var route = string.Join(",", context.Request.RouteValues.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
                var requestValues = string.Join(",", command.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .OrderBy(x => x.Name, StringComparer.Ordinal)
                    .Select(property => $"{property.Name}={Format(property.GetValue(command))}"));
                context.Response.Headers["X-Runtime-Capture-Binding"] = $"route={route};request={requestValues}";
            }

            return (T)RuntimeReplayRequestSender.CreateValue(typeof(T), typeof(T).Name)!;
        }

        private static string Format(object? value) => value switch
        {
            null => "<null>",
            IEnumerable values when value is not string => $"[{string.Join("|", values.Cast<object?>().Select(Format))}]",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"
        };
    }
}

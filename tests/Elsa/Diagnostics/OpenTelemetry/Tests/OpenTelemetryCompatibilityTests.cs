using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

internal sealed class OpenApiApprovalValidationException(string key, string reason) : Exception($"{key}: {reason}")
{
    public string Key { get; } = key;
}

public sealed class OpenTelemetryCompatibilityTests
{
    [Fact]
    public async Task Real_minimal_host_matches_frozen_http_and_route_contract()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();
        using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baselines", "otel-http-openapi-fastendpoints.json")));
        using var approvedDifferences = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baselines", "otel-approved-differences.json")));
        var expected = baseline.RootElement.GetProperty("http").EnumerateArray().ToArray();
        var approved = approvedDifferences.RootElement.GetProperty("differences").EnumerateArray()
            .ToLookup(item => $"{item.GetProperty("method").GetString()} {item.GetProperty("path").GetString()}", StringComparer.Ordinal);
        var approvedEntries = approvedDifferences.RootElement.GetProperty("differences").EnumerateArray().ToArray();
        var cases = expected.Select(item => (Method: item.GetProperty("method").GetString()!, Path: item.GetProperty("item3").GetString()!, Body: item.GetProperty("item1").GetString() == "stream" ? null : item.GetProperty("item1").GetString() is { } name && name is "resources" or "traces" or "metrics" or "logs" ? "{}" : item.GetProperty("item1").GetString()?.StartsWith("otlp-", StringComparison.Ordinal) == true ? "" : null)).ToArray();
        var consumedKeys = cases.Select(testCase => $"{testCase.Method} {testCase.Path}")
            .Concat(baseline.RootElement.GetProperty("openApi").GetProperty("paths").EnumerateObject().SelectMany(path => path.Value.EnumerateObject().Select(method => $"{method.Name.ToUpperInvariant()} {path.Name}")))
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(approvedEntries, item => Assert.Contains($"{item.GetProperty("method").GetString()} {item.GetProperty("path").GetString()}", consumedKeys));
        Assert.Equal(11, approvedEntries.Count(item => item.TryGetProperty("beforeOperationId", out _)));
        var observedStatusKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < cases.Length; index++)
        {
            var testCase = cases[index];
            using var request = new HttpRequestMessage(new HttpMethod(testCase.Method), testCase.Path);
            if (testCase.Body is not null)
                request.Content = new StringContent(testCase.Body, Encoding.UTF8, testCase.Body.Length == 0 ? "application/x-protobuf" : "application/json");
            using var response = await client.SendAsync(request, testCase.Path.Contains("stream", StringComparison.Ordinal) ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
            var expectedStatus = expected[index].GetProperty("status").GetInt32();
            var actualStatus = (int)response.StatusCode;
            var expectedContentType = expected[index].GetProperty("contentType").GetString() ?? string.Empty;
            var expectedBody = expected[index].GetProperty("body").GetString() ?? string.Empty;
            var actualContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
            var actualBody = testCase.Path.Contains("stream", StringComparison.Ordinal)
                ? string.Empty
                : await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedContentType, actualContentType);
            Assert.Equal(expectedBody, actualBody);
            if (expected[index].TryGetProperty("headers", out var expectedHeaders))
            {
                foreach (var header in expectedHeaders.EnumerateObject())
                {
                    var actualHeader = response.Headers.TryGetValues(header.Name, out var values)
                        ? values.Single()
                        : response.Content.Headers.TryGetValues(header.Name, out var contentValues) ? contentValues.Single() : null;
                    Assert.Equal(header.Value.GetString(), actualHeader);
                }
            }
            else if (expectedStatus == StatusCodes.Status302Found)
                Assert.NotNull(response.Headers.Location);
            if (testCase.Path.StartsWith("/elsa/otlp/", StringComparison.Ordinal))
                Assert.Null(response.Headers.Location);
            var key = $"{testCase.Method} {testCase.Path}";
            if (expectedStatus != actualStatus)
            {
                var difference = approved[key].SingleOrDefault(item => item.TryGetProperty("beforeStatus", out _));
                Assert.True(difference.ValueKind != JsonValueKind.Undefined, $"Unregistered compatibility delta: {key} {expectedStatus}->{actualStatus}");
                Assert.Equal(expectedStatus, difference.GetProperty("beforeStatus").GetInt32());
                Assert.Equal(actualStatus, difference.GetProperty("afterStatus").GetInt32());
                observedStatusKeys.Add(key);
            }
        }
        Assert.Equal(
            approvedEntries.Where(item => item.TryGetProperty("beforeStatus", out _)).Select(item => $"{item.GetProperty("method").GetString()} {item.GetProperty("path").GetString()}").Order(StringComparer.Ordinal),
            observedStatusKeys.Order(StringComparer.Ordinal));

        var routes = host.Services.GetRequiredService<EndpointDataSourceAccessor>().Routes;
        var migrated = routes.Where(route => route.Route.StartsWith("/diagnostics/opentelemetry", StringComparison.Ordinal) || route.Route.StartsWith("/_elsa/studio/diagnostics/opentelemetry", StringComparison.Ordinal)).ToArray();
        Assert.Equal(8, migrated.Length);
        Assert.Equal(3, routes.Count(route => route.Route.StartsWith("/elsa/otlp/v1/", StringComparison.Ordinal)));

        using var openApi = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var baselineOpenApi = baseline.RootElement.GetProperty("openApi");
        var actualOpenApi = openApi.RootElement;
        var approvalRoot = approvedDifferences.RootElement;
        var routeApprovals = approvalRoot.GetProperty("differences").EnumerateArray()
            .Where(item => item.TryGetProperty("method", out _) && item.TryGetProperty("path", out _))
            .ToArray();
        Assert.Equal(approvedEntries.Length, routeApprovals.Length);
        var componentApprovals = approvalRoot.TryGetProperty("componentDifferences", out var componentDifferenceArray)
            ? componentDifferenceArray.EnumerateArray().ToArray()
            : [];
        var documentApprovals = approvalRoot.TryGetProperty("documentDifferences", out var documentDifferenceArray)
            ? documentDifferenceArray.EnumerateArray().ToArray()
            : [];
        var approvedFacetKeys = OpenApiApprovalValidator.Validate(
            baselineOpenApi,
            actualOpenApi,
            routeApprovals,
            componentApprovals,
            documentApprovals);
        var consumedApprovals = new HashSet<string>(StringComparer.Ordinal);
        var baselinePaths = baselineOpenApi.GetProperty("paths").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        var actualPaths = actualOpenApi.GetProperty("paths").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(baselinePaths, actualPaths);

        foreach (var path in baselinePaths)
        {
            var baselinePath = baselineOpenApi.GetProperty("paths").GetProperty(path);
            var actualPath = actualOpenApi.GetProperty("paths").GetProperty(path);
            var baselineMethods = baselinePath.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
            var actualMethods = actualPath.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(baselineMethods, actualMethods);

            foreach (var method in baselineMethods)
            {
                var key = $"{method.ToUpperInvariant()} {path}";
                var before = baselinePath.GetProperty(method);
                var after = actualPath.GetProperty(method);
                var beforeNode = JsonNode.Parse(before.GetRawText())!.AsObject();
                var afterNode = JsonNode.Parse(after.GetRawText())!.AsObject();
                var approvals = routeApprovals.Where(item =>
                    string.Equals($"{item.GetProperty("method").GetString()?.ToUpperInvariant()} {item.GetProperty("path").GetString()}", key, StringComparison.Ordinal)).ToArray();

                foreach (var approval in approvals)
                {
                    if (approval.TryGetProperty("beforeOperationId", out var beforeOperationId))
                    {
                        Assert.Equal(beforeOperationId.GetString(), before.GetProperty("operationId").GetString());
                        Assert.Equal(approval.GetProperty("afterOperationId").GetString(), after.GetProperty("operationId").GetString());
                        beforeNode.Remove("operationId");
                        afterNode.Remove("operationId");
                        consumedApprovals.Add(OpenApiApprovalValidator.RouteFacetKey(key, "operationId"));
                    }

                    if (approval.TryGetProperty("beforeTags", out var beforeTags))
                    {
                        Assert.True(JsonElement.DeepEquals(beforeTags, before.GetProperty("tags")));
                        Assert.True(JsonElement.DeepEquals(approval.GetProperty("afterTags"), after.GetProperty("tags")));
                        beforeNode.Remove("tags");
                        afterNode.Remove("tags");
                        consumedApprovals.Add(OpenApiApprovalValidator.RouteFacetKey(key, "tags"));
                    }

                    if (approval.TryGetProperty("beforeResponseStatuses", out var beforeStatuses))
                    {
                        Assert.True(JsonElement.DeepEquals(beforeStatuses, JsonDocument.Parse($"[{string.Join(',', before.GetProperty("responses").EnumerateObject().Select(item => JsonSerializer.Serialize(item.Name)))}]").RootElement));
                        Assert.True(JsonElement.DeepEquals(approval.GetProperty("afterResponseStatuses"), JsonDocument.Parse($"[{string.Join(',', after.GetProperty("responses").EnumerateObject().Select(item => JsonSerializer.Serialize(item.Name)))}]").RootElement));
                        CompareResponseObjects(
                            beforeNode["responses"]!.AsObject(),
                            afterNode["responses"]!.AsObject(),
                            beforeStatuses,
                            approval.GetProperty("afterResponseStatuses"));
                        consumedApprovals.Add(OpenApiApprovalValidator.RouteFacetKey(key, "responseStatuses"));
                    }
                }

                Assert.True(JsonNode.DeepEquals(beforeNode, afterNode), $"Unapproved OpenAPI change at {key}. Before={beforeNode}; After={afterNode}");
            }
        }

        var baselineSchemas = baselineOpenApi.GetProperty("components").GetProperty("schemas");
        var actualSchemas = actualOpenApi.GetProperty("components").GetProperty("schemas");
        var beforeSchemas = JsonNode.Parse(baselineSchemas.GetRawText())!.AsObject();
        var afterSchemas = JsonNode.Parse(actualSchemas.GetRawText())!.AsObject();
        foreach (var approval in componentApprovals)
        {
            var name = approval.GetProperty("name").GetString()!;
            Assert.Equal("schema", approval.GetProperty("kind").GetString());
            Assert.True(approval.GetProperty("before").ValueKind is JsonValueKind.Null or JsonValueKind.Object);
            if (approval.GetProperty("before").ValueKind == JsonValueKind.Null)
                Assert.False(baselineSchemas.TryGetProperty(name, out _));
            else
                Assert.True(JsonElement.DeepEquals(approval.GetProperty("before"), baselineSchemas.GetProperty(name)));
            Assert.True(JsonElement.DeepEquals(approval.GetProperty("after"), actualSchemas.GetProperty(name)));
            beforeSchemas.Remove(name);
            afterSchemas.Remove(name);
            consumedApprovals.Add(OpenApiApprovalValidator.ComponentFacetKey(name));
        }
        Assert.True(JsonNode.DeepEquals(beforeSchemas, afterSchemas), $"Unapproved OpenAPI component change. Before={beforeSchemas}; After={afterSchemas}");

        var beforeDocument = JsonNode.Parse(baselineOpenApi.GetRawText())!.AsObject();
        var afterDocument = JsonNode.Parse(actualOpenApi.GetRawText())!.AsObject();
        foreach (var approval in documentApprovals)
        {
            var property = approval.GetProperty("property").GetString()!;
            Assert.True(JsonElement.DeepEquals(approval.GetProperty("before"), baselineOpenApi.GetProperty(property)));
            Assert.True(JsonElement.DeepEquals(approval.GetProperty("after"), actualOpenApi.GetProperty(property)));
            beforeDocument.Remove(property);
            afterDocument.Remove(property);
            consumedApprovals.Add(OpenApiApprovalValidator.DocumentFacetKey(property));
        }
        beforeDocument.Remove("paths");
        afterDocument.Remove("paths");
        beforeDocument["components"]!.AsObject().Remove("schemas");
        afterDocument["components"]!.AsObject().Remove("schemas");
        Assert.True(JsonNode.DeepEquals(beforeDocument, afterDocument), $"Unapproved OpenAPI document change. Before={beforeDocument}; After={afterDocument}");

        Assert.Equal(approvedFacetKeys.Order(StringComparer.Ordinal), consumedApprovals.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Anonymous_redirects_match_the_separate_deleted_fastendpoints_fixture()
    {
        using var host = await StartHostAsync();
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baselines", "otel-http-redirect-fastendpoints.json")));
        var client = host.GetTestClient();

        foreach (var expected in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            using var request = new HttpRequestMessage(new HttpMethod(expected.GetProperty("method").GetString()!), expected.GetProperty("path").GetString()!);
            using var response = await client.SendAsync(request);

            Assert.Equal(expected.GetProperty("status").GetInt32(), (int)response.StatusCode);
            Assert.Equal(expected.GetProperty("location").GetString(), response.Headers.Location?.ToString());
        }
    }

    [Fact]
    public void OpenApi_comparer_rejects_mutation_in_an_unchanged_common_response()
    {
        var before = JsonNode.Parse("{\"200\":{\"description\":\"before\"},\"401\":{\"description\":\"unauthorized\"}}")!.AsObject();
        var after = JsonNode.Parse("{\"200\":{\"description\":\"mutated\"},\"401\":{\"description\":\"unauthorized\"}}")!.AsObject();
        using var statuses = JsonDocument.Parse("[\"200\",\"401\"]");

        Assert.Throws<Xunit.Sdk.TrueException>(() => CompareResponseObjects(before, after, statuses.RootElement, statuses.RootElement));
    }

    [Fact]
    public void OpenApi_approval_validator_rejects_duplicate_and_noop_route_facets()
    {
        const string route = "{\"method\":\"GET\",\"path\":\"/sample\",\"beforeOperationId\":\"Before\",\"afterOperationId\":\"After\"}";
        AssertApprovalFailure(() => ValidateSyntheticApprovals($"[{route},{route}]", "[]", "[]"), "route:GET /sample", "Duplicate approval facet.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/sample\",\"beforeOperationId\":\"Before\",\"afterOperationId\":\"Before\"}]",
            "[]",
            "[]"), "route:GET /sample", "operationId before and after values must differ.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/sample\",\"beforeOperationId\":\"Before\"}]",
            "[]",
            "[]"), "route:GET /sample", "Approval facet requires both beforeOperationId and afterOperationId.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/sample\",\"afterOperationId\":\"After\"}]",
            "[]",
            "[]"), "route:GET /sample", "Approval facet requires both beforeOperationId and afterOperationId.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/sample\",\"reason\":\"stale\"}]",
            "[]",
            "[]"), "route:GET /sample", "Approval does not declare a recognized changed facet.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/stale\",\"beforeOperationId\":\"Before\",\"afterOperationId\":\"After\"}]",
            "[]",
            "[]"), "route:GET /stale", "Route is absent from the before document.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/sample\",\"beforeOperationId\":\"Before\",\"afterOperationId\":\"After\",\"extra\":true}]",
            "[]",
            "[]"), "route:GET /sample", "Unknown approval property 'extra'.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/sample\",\"beforeOperationId\":\"Wrong\",\"afterOperationId\":\"After\"}]",
            "[]",
            "[]"), "route:GET /sample", "before operationId does not match document.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[{\"method\":\"GET\",\"path\":\"/sample\",\"beforeOperationId\":\"Before\",\"afterOperationId\":\"Wrong\"}]",
            "[]",
            "[]"), "route:GET /sample", "after operationId does not match document.");
    }

    [Fact]
    public void OpenApi_approval_validator_rejects_duplicate_and_noop_schema_facets()
    {
        const string schema = "{\"kind\":\"schema\",\"name\":\"Sample\",\"before\":{\"type\":\"object\"},\"after\":{\"type\":\"string\"}}";
        AssertApprovalFailure(() => ValidateSyntheticApprovals("[]", $"[{schema},{schema}]", "[]"), "component:schema/Sample", "Duplicate approval facet.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[{\"kind\":\"schema\",\"name\":\"Sample\",\"before\":{\"type\":\"object\"},\"after\":{\"type\":\"object\"}}]",
            "[]"), "component:schema/Sample", "before and after values must differ.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[{\"kind\":\"schema\",\"name\":\"Sample\",\"after\":{\"type\":\"string\"}}]",
            "[]"), "component:schema/Sample", "Approval is missing 'before'.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[{\"kind\":\"schema\",\"name\":\"Sample\",\"before\":{\"type\":\"object\"}}]",
            "[]"), "component:schema/Sample", "Approval is missing 'after'.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[{\"kind\":\"schema\",\"name\":\"Sample\",\"before\":{\"type\":\"object\"},\"after\":{\"type\":\"string\"},\"extra\":true}]",
            "[]"), "component:schema/Sample", "Unknown approval property 'extra'.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[{\"kind\":\"schema\",\"name\":\"Sample\",\"before\":{\"type\":\"array\"},\"after\":{\"type\":\"string\"}}]",
            "[]"), "component:schema/Sample", "before value does not match document.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[{\"kind\":\"schema\",\"name\":\"Sample\",\"before\":{\"type\":\"object\"},\"after\":{\"type\":\"array\"}}]",
            "[]"), "component:schema/Sample", "after value does not match document.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[{\"kind\":\"schema\",\"name\":\"Missing\",\"before\":null,\"after\":{\"type\":\"string\"}}]",
            "[]"), "component:schema/Missing", "after schema is absent from document.");
    }

    [Fact]
    public void OpenApi_approval_validator_rejects_duplicate_and_noop_document_facets()
    {
        const string document = "{\"property\":\"tags\",\"before\":[{\"name\":\"Before\"}],\"after\":[{\"name\":\"After\"}]}";
        AssertApprovalFailure(() => ValidateSyntheticApprovals("[]", "[]", $"[{document},{document}]"), "document:tags", "Duplicate approval facet.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[]",
            "[{\"property\":\"tags\",\"before\":[{\"name\":\"Before\"}],\"after\":[{\"name\":\"Before\"}]}]"), "document:tags", "before and after values must differ.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[]",
            "[{\"property\":\"tags\",\"after\":[{\"name\":\"After\"}]}]"), "document:tags", "Approval is missing 'before'.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[]",
            "[{\"property\":\"tags\",\"before\":[{\"name\":\"Before\"}]}]"), "document:tags", "Approval is missing 'after'.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[]",
            "[{\"property\":\"tags\",\"before\":[{\"name\":\"Wrong\"}],\"after\":[{\"name\":\"After\"}]}]"), "document:tags", "before value does not match document.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[]",
            "[{\"property\":\"tags\",\"before\":[{\"name\":\"Before\"}],\"after\":[{\"name\":\"Wrong\"}]}]"), "document:tags", "after value does not match document.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[]",
            "[{\"property\":\"tags\",\"before\":[{\"name\":\"Before\"}],\"after\":[{\"name\":\"After\"}],\"extra\":true}]"), "document:tags", "Unknown approval property 'extra'.");
        AssertApprovalFailure(() => ValidateSyntheticApprovals(
            "[]",
            "[]",
            "[{\"property\":\"missing\",\"before\":[],\"after\":[{\"name\":\"After\"}]}]"), "document:missing", "before property is absent from document.");
    }

    private static void AssertApprovalFailure(Action action, string key, string reason)
    {
        var exception = Assert.Throws<OpenApiApprovalValidationException>(action);
        Assert.Equal(key, exception.Key);
        Assert.Equal($"{key}: {reason}", exception.Message);
    }

    private static void ValidateSyntheticApprovals(string routes, string components, string documents)
    {
        using var before = JsonDocument.Parse("{\"paths\":{\"/sample\":{\"get\":{\"operationId\":\"Before\",\"tags\":[{\"name\":\"Before\"}],\"responses\":{\"200\":{}}}}},\"components\":{\"schemas\":{\"Sample\":{\"type\":\"object\"}}},\"tags\":[{\"name\":\"Before\"}]}");
        using var after = JsonDocument.Parse("{\"paths\":{\"/sample\":{\"get\":{\"operationId\":\"After\",\"tags\":[{\"name\":\"After\"}],\"responses\":{\"200\":{}}}}},\"components\":{\"schemas\":{\"Sample\":{\"type\":\"string\"}}},\"tags\":[{\"name\":\"After\"}]}");
        using var routeDocument = JsonDocument.Parse(routes);
        using var componentDocument = JsonDocument.Parse(components);
        using var documentDocument = JsonDocument.Parse(documents);

        OpenApiApprovalValidator.Validate(
            before.RootElement,
            after.RootElement,
            routeDocument.RootElement.EnumerateArray().ToArray(),
            componentDocument.RootElement.EnumerateArray().ToArray(),
            documentDocument.RootElement.EnumerateArray().ToArray());
    }

    private static class OpenApiApprovalValidator
    {
        private static readonly IReadOnlySet<string> RouteApprovalProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "method", "path", "reason",
            "beforeOperationId", "afterOperationId",
            "beforeTags", "afterTags",
            "beforeResponseStatuses", "afterResponseStatuses"
        };

        private static readonly IReadOnlySet<string> ComponentApprovalProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "kind", "name", "before", "after", "reason"
        };

        private static readonly IReadOnlySet<string> DocumentApprovalProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "property", "before", "after", "reason"
        };

        public static IReadOnlySet<string> Validate(
            JsonElement beforeDocument,
            JsonElement afterDocument,
            IReadOnlyList<JsonElement> routeApprovals,
            IReadOnlyList<JsonElement> componentApprovals,
            IReadOnlyList<JsonElement> documentApprovals)
        {
            var facetKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var approval in routeApprovals)
                ValidateRouteApproval(beforeDocument, afterDocument, approval, facetKeys);

            foreach (var approval in componentApprovals)
                ValidateComponentApproval(beforeDocument, afterDocument, approval, facetKeys);

            foreach (var approval in documentApprovals)
                ValidateDocumentApproval(beforeDocument, afterDocument, approval, facetKeys);

            return facetKeys;
        }

        public static string RouteFacetKey(string routeKey, string facet) => $"route:{routeKey}:{facet}";

        public static string ComponentFacetKey(string name) => $"component:schema/{name}";

        public static string DocumentFacetKey(string property) => $"document:{property}";

        private static void ValidateRouteApproval(
            JsonElement beforeDocument,
            JsonElement afterDocument,
            JsonElement approval,
            ISet<string> facetKeys)
        {
            var method = approval.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
            var path = approval.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
            var routeKey = $"route:{method?.ToUpperInvariant() ?? "<unknown>"} {path ?? "<unknown>"}";
            Require(method is not null, routeKey, "Approval is missing 'method'.");
            Require(path is not null, routeKey, "Approval is missing 'path'.");
            ValidateProperties(approval, RouteApprovalProperties, routeKey);
            Require(TryGetOperation(beforeDocument, path!, method!, out var beforeOperation), routeKey, "Route is absent from the before document.");
            Require(TryGetOperation(afterDocument, path!, method!, out var afterOperation), routeKey, "Route is absent from the after document.");

            var facetCount = 0;
            if (TryGetChangedPair(approval, "beforeOperationId", "afterOperationId", routeKey, "operationId", out var beforeOperationId, out var afterOperationId))
            {
                AssertOpenApiProperty(beforeOperation, "operationId", beforeOperationId, routeKey, "before");
                AssertOpenApiProperty(afterOperation, "operationId", afterOperationId, routeKey, "after");
                AddFacet(facetKeys, RouteFacetKey($"{method!.ToUpperInvariant()} {path}", "operationId"), routeKey);
                facetCount++;
            }

            if (TryGetChangedPair(approval, "beforeTags", "afterTags", routeKey, "tags", out var beforeTags, out var afterTags))
            {
                AssertOpenApiProperty(beforeOperation, "tags", beforeTags, routeKey, "before");
                AssertOpenApiProperty(afterOperation, "tags", afterTags, routeKey, "after");
                AddFacet(facetKeys, RouteFacetKey($"{method!.ToUpperInvariant()} {path}", "tags"), routeKey);
                facetCount++;
            }

            if (TryGetChangedPair(approval, "beforeResponseStatuses", "afterResponseStatuses", routeKey, "responseStatuses", out var beforeStatuses, out var afterStatuses))
            {
                Require(JsonElement.DeepEquals(beforeStatuses, GetResponseStatuses(beforeOperation)), routeKey, "before response statuses do not match document.");
                Require(JsonElement.DeepEquals(afterStatuses, GetResponseStatuses(afterOperation)), routeKey, "after response statuses do not match document.");
                AddFacet(facetKeys, RouteFacetKey($"{method!.ToUpperInvariant()} {path}", "responseStatuses"), routeKey);
                facetCount++;
            }

            Require(facetCount > 0, routeKey, "Approval does not declare a recognized changed facet.");
        }

        private static void ValidateComponentApproval(
            JsonElement beforeDocument,
            JsonElement afterDocument,
            JsonElement approval,
            ISet<string> facetKeys)
        {
            var name = approval.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var key = ComponentFacetKey(name ?? "<unknown>");
            ValidateProperties(approval, ComponentApprovalProperties, key);
            var kind = RequiredProperty(approval, "kind", key);
            Require(kind.GetString() == "schema", key, "kind must be 'schema'.");
            var before = RequiredProperty(approval, "before", key);
            var after = RequiredProperty(approval, "after", key);
            Require(name is not null, key, "Approval is missing 'name'.");
            Require(!JsonElement.DeepEquals(before, after), key, "before and after values must differ.");

            var beforeSchemas = beforeDocument.GetProperty("components").GetProperty("schemas");
            var afterSchemas = afterDocument.GetProperty("components").GetProperty("schemas");
            AssertSchemaValue(beforeSchemas, name!, before, "before", key);
            AssertSchemaValue(afterSchemas, name!, after, "after", key);
            AddFacet(facetKeys, key, key);
        }

        private static void ValidateDocumentApproval(
            JsonElement beforeDocument,
            JsonElement afterDocument,
            JsonElement approval,
            ISet<string> facetKeys)
        {
            var property = approval.TryGetProperty("property", out var propertyElement) ? propertyElement.GetString() : null;
            var key = DocumentFacetKey(property ?? "<unknown>");
            ValidateProperties(approval, DocumentApprovalProperties, key);
            Require(property is not null, key, "Approval is missing 'property'.");
            var before = RequiredProperty(approval, "before", key);
            var after = RequiredProperty(approval, "after", key);
            Require(!JsonElement.DeepEquals(before, after), key, "before and after values must differ.");
            Require(beforeDocument.TryGetProperty(property!, out var actualBefore), key, "before property is absent from document.");
            Require(afterDocument.TryGetProperty(property!, out var actualAfter), key, "after property is absent from document.");
            Require(JsonElement.DeepEquals(before, actualBefore), key, "before value does not match document.");
            Require(JsonElement.DeepEquals(after, actualAfter), key, "after value does not match document.");
            AddFacet(facetKeys, key, key);
        }

        private static bool TryGetChangedPair(
            JsonElement approval,
            string beforeName,
            string afterName,
            string routeKey,
            string facet,
            out JsonElement before,
            out JsonElement after)
        {
            var hasBefore = approval.TryGetProperty(beforeName, out before);
            var hasAfter = approval.TryGetProperty(afterName, out after);
            Require(hasBefore == hasAfter, routeKey, $"Approval facet requires both {beforeName} and {afterName}.");
            if (!hasBefore)
                return false;

            Require(!JsonElement.DeepEquals(before, after), routeKey, $"{facet} before and after values must differ.");
            return true;
        }

        private static bool TryGetOperation(JsonElement document, string path, string method, out JsonElement operation)
        {
            operation = default;
            return document.GetProperty("paths").TryGetProperty(path, out var pathItem)
                && pathItem.TryGetProperty(method.ToLowerInvariant(), out operation);
        }

        private static JsonElement GetResponseStatuses(JsonElement operation)
        {
            using var statuses = JsonDocument.Parse($"[{string.Join(',', operation.GetProperty("responses").EnumerateObject().Select(item => JsonSerializer.Serialize(item.Name)))}]");
            return statuses.RootElement.Clone();
        }

        private static void AssertOpenApiProperty(JsonElement operation, string property, JsonElement expected, string routeKey, string side)
        {
            Require(operation.TryGetProperty(property, out var actual), routeKey, $"{side} {property} is absent from document.");
            Require(JsonElement.DeepEquals(expected, actual), routeKey, $"{side} {property} does not match document.");
        }

        private static void AssertSchemaValue(JsonElement schemas, string name, JsonElement expected, string side, string key)
        {
            var exists = schemas.TryGetProperty(name, out var actual);
            if (expected.ValueKind == JsonValueKind.Null)
            {
                Require(!exists, key, $"{side} schema is present in document.");
                return;
            }

            Require(exists, key, $"{side} schema is absent from document.");
            Require(JsonElement.DeepEquals(expected, actual), key, $"{side} value does not match document.");
        }

        private static JsonElement RequiredProperty(JsonElement approval, string property, string key)
        {
            Require(approval.TryGetProperty(property, out var value), key, $"Approval is missing '{property}'.");
            return value;
        }

        private static void ValidateProperties(JsonElement approval, IReadOnlySet<string> allowedProperties, string key)
        {
            foreach (var property in approval.EnumerateObject())
                Require(allowedProperties.Contains(property.Name), key, $"Unknown approval property '{property.Name}'.");
        }

        private static void AddFacet(ISet<string> facetKeys, string key, string diagnosticKey) =>
            Require(facetKeys.Add(key), diagnosticKey, "Duplicate approval facet.");

        private static void Require(bool condition, string key, string reason)
        {
            if (!condition)
                throw new OpenApiApprovalValidationException(key, reason);
        }
    }

    private static void CompareResponseObjects(
        JsonObject beforeResponses,
        JsonObject afterResponses,
        JsonElement beforeStatuses,
        JsonElement afterStatuses)
    {
        var beforeKeys = beforeStatuses.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        var afterKeys = afterStatuses.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        foreach (var status in beforeKeys.Except(afterKeys).Concat(afterKeys.Except(beforeKeys)))
        {
            beforeResponses.Remove(status);
            afterResponses.Remove(status);
        }

        Assert.True(JsonNode.DeepEquals(beforeResponses, afterResponses), $"Unapproved OpenAPI response change. Before={beforeResponses}; After={afterResponses}");
    }

    private static async Task<IHost> StartHostAsync()
    {
        var host = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
            webHost.ConfigureServices(services =>
            {
                services.AddElsaEndpoints();
                services.AddLogging();
                services.AddRouting();
                services.AddOpenApi();
                services.AddAuthentication("otel-test").AddCookie("otel-test");
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions();
                services.AddSingleton<EndpointDataSourceAccessor>();
                new OpenTelemetryFeature().ConfigureServices(services);
            });
            webHost.Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Loopback;
                    await next();
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    new OpenTelemetryFeature().MapEndpoints(endpoints, null);
                    endpoints.MapOpenApi();
                    endpoints.ServiceProvider.GetRequiredService<EndpointDataSourceAccessor>().Routes = endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(route => new RouteInfo(route.RoutePattern.RawText ?? string.Empty)).ToArray();
                });
            });
        }).Build();
        await host.StartAsync();
        return host;
    }

    private sealed class EndpointDataSourceAccessor
    {
        public RouteInfo[] Routes { get; set; } = [];
    }

    private sealed record RouteInfo(string Route);
}

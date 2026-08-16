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

                foreach (var (approval, index) in approvals.Select((item, index) => (item, index)))
                {
                    var approvalKey = $"route:{key}#{index}";
                    if (approval.TryGetProperty("beforeOperationId", out var beforeOperationId))
                    {
                        Assert.Equal(beforeOperationId.GetString(), before.GetProperty("operationId").GetString());
                        Assert.Equal(approval.GetProperty("afterOperationId").GetString(), after.GetProperty("operationId").GetString());
                        beforeNode.Remove("operationId");
                        afterNode.Remove("operationId");
                    }

                    if (approval.TryGetProperty("beforeTags", out var beforeTags))
                    {
                        Assert.True(JsonElement.DeepEquals(beforeTags, before.GetProperty("tags")));
                        Assert.True(JsonElement.DeepEquals(approval.GetProperty("afterTags"), after.GetProperty("tags")));
                        beforeNode.Remove("tags");
                        afterNode.Remove("tags");
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
                    }

                    consumedApprovals.Add(approvalKey);
                }

                Assert.True(JsonNode.DeepEquals(beforeNode, afterNode), $"Unapproved OpenAPI change at {key}. Before={beforeNode}; After={afterNode}");
            }
        }

        var baselineSchemas = baselineOpenApi.GetProperty("components").GetProperty("schemas");
        var actualSchemas = actualOpenApi.GetProperty("components").GetProperty("schemas");
        var beforeSchemas = JsonNode.Parse(baselineSchemas.GetRawText())!.AsObject();
        var afterSchemas = JsonNode.Parse(actualSchemas.GetRawText())!.AsObject();
        var componentApprovals = approvalRoot.TryGetProperty("componentDifferences", out var componentDifferenceArray)
            ? componentDifferenceArray.EnumerateArray().ToArray()
            : [];
        foreach (var (approval, index) in componentApprovals.Select((item, index) => (item, index)))
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
            consumedApprovals.Add($"component:schema/{name}#{index}");
        }
        Assert.True(JsonNode.DeepEquals(beforeSchemas, afterSchemas), $"Unapproved OpenAPI component change. Before={beforeSchemas}; After={afterSchemas}");

        var documentApprovals = approvalRoot.TryGetProperty("documentDifferences", out var documentDifferenceArray)
            ? documentDifferenceArray.EnumerateArray().ToArray()
            : [];
        var beforeDocument = JsonNode.Parse(baselineOpenApi.GetRawText())!.AsObject();
        var afterDocument = JsonNode.Parse(actualOpenApi.GetRawText())!.AsObject();
        foreach (var (approval, index) in documentApprovals.Select((item, index) => (item, index)))
        {
            var property = approval.GetProperty("property").GetString()!;
            Assert.True(JsonElement.DeepEquals(approval.GetProperty("before"), baselineOpenApi.GetProperty(property)));
            Assert.True(JsonElement.DeepEquals(approval.GetProperty("after"), actualOpenApi.GetProperty(property)));
            beforeDocument.Remove(property);
            afterDocument.Remove(property);
            consumedApprovals.Add($"document:{property}#{index}");
        }
        beforeDocument.Remove("paths");
        afterDocument.Remove("paths");
        beforeDocument["components"]!.AsObject().Remove("schemas");
        afterDocument["components"]!.AsObject().Remove("schemas");
        Assert.True(JsonNode.DeepEquals(beforeDocument, afterDocument), $"Unapproved OpenAPI document change. Before={beforeDocument}; After={afterDocument}");

        var expectedApprovalCount = routeApprovals.Length + componentApprovals.Length + documentApprovals.Length;
        Assert.Equal(expectedApprovalCount, consumedApprovals.Count);
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

using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
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
        Assert.Equal(3, approvedEntries.Count(item => item.TryGetProperty("beforeStatus", out _)));
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
            if (expectedStatus == StatusCodes.Status302Found)
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
        var operations = new Dictionary<string, (string Method, string OperationId)>(StringComparer.Ordinal)
        {
            ["/diagnostics/opentelemetry/resources/search"] = ("post", "OpenTelemetryResourcesSearch"),
            ["/diagnostics/opentelemetry/traces/search"] = ("post", "OpenTelemetryTracesSearch"),
            ["/diagnostics/opentelemetry/metrics/search"] = ("post", "OpenTelemetryMetricsSearch"),
            ["/diagnostics/opentelemetry/logs/search"] = ("post", "OpenTelemetryLogsSearch"),
            ["/diagnostics/opentelemetry/traces/{traceId}"] = ("get", "OpenTelemetryTraceDetail"),
            ["/diagnostics/opentelemetry/storage"] = ("get", "OpenTelemetryStorage"),
            ["/diagnostics/opentelemetry/collector-configuration"] = ("get", "OpenTelemetryCollectorConfiguration"),
            ["/_elsa/studio/diagnostics/opentelemetry/stream"] = ("get", "OpenTelemetryStream"),
            ["/elsa/otlp/v1/traces"] = ("post", "OpenTelemetryOtlpTraces"),
            ["/elsa/otlp/v1/metrics"] = ("post", "OpenTelemetryOtlpMetrics"),
            ["/elsa/otlp/v1/logs"] = ("post", "OpenTelemetryOtlpLogs")
        };
        var openApiPaths = openApi.RootElement.GetProperty("paths");
        var baselineOpenApiPaths = baseline.RootElement.GetProperty("openApi").GetProperty("paths");
        var approvedOpenApi = approvedDifferences.RootElement.GetProperty("differences").EnumerateArray()
            .Where(item => item.TryGetProperty("beforeOperationId", out _))
            .ToLookup(item => $"{item.GetProperty("method").GetString()?.ToUpperInvariant()} {item.GetProperty("path").GetString()}", StringComparer.Ordinal);
        var approvedStatusPaths = approvedDifferences.RootElement.GetProperty("differences").EnumerateArray()
            .Where(item => item.TryGetProperty("beforeResponseStatuses", out _))
            .ToLookup(item => $"{item.GetProperty("method").GetString()?.ToUpperInvariant()} {item.GetProperty("path").GetString()}", StringComparer.Ordinal);
        foreach (var (path, operation) in operations)
        {
            Assert.True(openApiPaths.TryGetProperty(path, out var pathItem), $"OpenAPI path missing: {path}; actual={string.Join(",", openApiPaths.EnumerateObject().Select(property => property.Name))}");
            Assert.True(pathItem.TryGetProperty(operation.Method, out var documentOperation), $"OpenAPI method missing: {operation.Method} {path}");
            Assert.Equal(operation.OperationId, documentOperation.GetProperty("operationId").GetString());
            Assert.Contains("OpenTelemetry", documentOperation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
            var openApiDifference = approved[$"{operation.Method.ToUpperInvariant()} {path}"].SingleOrDefault(item => item.TryGetProperty("beforeOperationId", out _));
            Assert.True(openApiDifference.ValueKind != JsonValueKind.Undefined, $"Unregistered OpenAPI delta: {operation.Method.ToUpperInvariant()} {path}");
            Assert.Equal(openApiDifference.GetProperty("afterOperationId").GetString(), documentOperation.GetProperty("operationId").GetString());
            Assert.Equal(openApiDifference.GetProperty("afterTags").EnumerateArray().Select(tag => tag.GetString()), documentOperation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));

            var baselineOperation = baselineOpenApiPaths.GetProperty(path).GetProperty(operation.Method);
            if (baselineOperation.TryGetProperty("requestBody", out var baselineRequestBody))
                Assert.True(documentOperation.TryGetProperty("requestBody", out var actualRequestBody) && JsonElement.DeepEquals(baselineRequestBody, actualRequestBody), $"OpenAPI requestBody changed: {operation.Method} {path}");
            var baselineResponseStatuses = baselineOperation.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToArray();
            var actualResponseStatuses = documentOperation.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToArray();
            if (!baselineResponseStatuses.SequenceEqual(actualResponseStatuses, StringComparer.Ordinal))
            {
                var statusDifference = approvedStatusPaths[$"{operation.Method.ToUpperInvariant()} {path}"].SingleOrDefault();
                Assert.True(statusDifference.ValueKind != JsonValueKind.Undefined, $"Unregistered OpenAPI response delta: {operation.Method.ToUpperInvariant()} {path}; before={string.Join(',', baselineResponseStatuses)}; after={string.Join(',', actualResponseStatuses)}");
                Assert.Equal(statusDifference.GetProperty("afterResponseStatuses").EnumerateArray().Select(item => item.GetString()), actualResponseStatuses);
            }
        }
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
                    endpoints.MapOpenTelemetryOtlpReceiver();
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

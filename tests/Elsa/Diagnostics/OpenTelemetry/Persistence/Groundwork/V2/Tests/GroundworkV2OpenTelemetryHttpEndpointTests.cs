using CShells.FastEndpoints.Contracts;
using Elsa.Api.FastEndpoints.Options;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using FastEndpoints;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2OpenTelemetryHttpEndpointTests
{
    [Fact]
    public async Task Trace_search_HTTP_endpoint_returns_the_v2_Groundwork_result()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-otel-v2-http-");
        var path = Path.Combine(directory.FullName, "diagnostics.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={path}");
            using var host = await StartHostAsync(connection);
            var store = host.Services.GetRequiredService<GroundworkOpenTelemetryStore>();
            Assert.Same(store, host.Services.GetRequiredService<IOpenTelemetryStore>());
            var now = new DateTimeOffset(2026, 8, 17, 6, 0, 0, TimeSpan.Zero);
            var resource = new TelemetryResource(
                "resource-http",
                "orders",
                null,
                "dotnet",
                new Dictionary<string, string?>(),
                now,
                TelemetryResourceStatus.Active);
            var trace = new TelemetryTrace(
                "trace-http",
                "root-http",
                "checkout",
                now.AddSeconds(-1),
                now,
                TimeSpan.FromSeconds(1),
                SpanStatus.Ok,
                [resource.Id],
                ["workflow-http"],
                1);
            await store.WriteAsync(
                DiagnosticsDrainBatchId.New(),
                new OpenTelemetryBatch([resource], [trace], [], [], [], []));

            using var response = await host.GetTestClient().PostAsJsonAsync(
                "/diagnostics/opentelemetry/traces/search",
                new OpenTelemetryTraceFilter { ServiceName = "orders", Take = 10 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            Assert.Equal(0, result.RootElement.GetProperty("droppedCount").GetInt64());
            var item = Assert.Single(result.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("trace-http", item.GetProperty("traceId").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<IHost> StartHostAsync(IStorageProviderConnection connection)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.Configure<ApiSecurityOptions>(options =>
                    {
                        options.AllowAnonymous = true;
                        options.ShellName = "groundwork-v2-http-proof";
                    });
                    new global::Elsa.Diagnostics.OpenTelemetry.OpenTelemetryFeature
                    {
                        MaxQuerySize = 100
                    }.ConfigureServices(services);
                    services.AddSingleton(connection);
                    new GroundworkOpenTelemetryPersistenceFeature().ConfigureServices(services);
                    services.AddFastEndpoints(options =>
                        options.Assemblies = [typeof(global::Elsa.Diagnostics.OpenTelemetry.OpenTelemetryFeature).Assembly]);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapFastEndpoints(config =>
                    {
                        using var scope = endpoints.ServiceProvider.CreateScope();
                        foreach (var configurator in scope.ServiceProvider.GetServices<IFastEndpointsConfigurator>())
                            configurator.Configure(config);
                    }));
                });
            })
            .Build();
        await host.StartAsync();
        return host;
    }
}

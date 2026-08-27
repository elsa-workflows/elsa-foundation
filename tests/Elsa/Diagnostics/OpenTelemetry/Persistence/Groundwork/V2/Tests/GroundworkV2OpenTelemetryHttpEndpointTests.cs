using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2OpenTelemetryHttpEndpointTests
{
    private const string TestAuthenticationType = "groundwork-v2-http-proof";

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
        var telemetry = new global::Elsa.Diagnostics.OpenTelemetry.OpenTelemetryFeature
        {
            MaxQuerySize = 100
        };
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
                webHost.ConfigureServices(services =>
                {
                    services.AddElsaEndpoints();
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            TestAuthenticationType
                        });
                    telemetry.ConfigureServices(services);
                    services.AddSingleton(connection);
                    new GroundworkOpenTelemetryPersistenceFeature().ConfigureServices(services);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.Use((context, next) =>
                    {
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(IdentityClaimTypes.Normalized, "v1"),
                            new Claim(IdentityClaimTypes.Permission, OpenTelemetryPermissions.Read)
                        ], TestAuthenticationType));
                        return next(context);
                    });
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => ((IWebShellFeature)telemetry).MapEndpoints(endpoints, null));
                });
            })
            .Build();
        await host.StartAsync();
        return host;
    }
}
